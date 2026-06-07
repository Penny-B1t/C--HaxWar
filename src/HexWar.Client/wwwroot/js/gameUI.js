// wwwroot/js/gameUI.js

/**
 * 게임 UI 컨트롤러
 */
class GameUI {
    constructor() {
        this.client = new GameClient();
        this.renderer = null;
        this.stateView = null;
        this.selectedFrom = null;
        this.selectedUnits = 0;
        this.pendingMoves = [];
        this.encounterData = null;
        this.optimisticMoves = []; // 서버 확인 전 로컬 임시 이동 데이터 보관

        // 라운드 타이머 관련 상태
        this.timerInterval = null;
        this.timeRemaining = 0;
        this.lastPhase = null;
        this.lastRound = null;
        this.isMyPlanningComplete = false;
    }

    initialize() {
        // SVG 렌더러 초기화
        const svg = document.getElementById('game-board');
        this.renderer = new GameRenderer(svg);

        // 이벤트 핸들러 바인딩
        this.client.onStateUpdate = (state) => this.handleStateUpdate(state);
        this.client.onEncounter = (data) => this.handleEncounter(data);
        this.client.onGameOver = (data) => this.handleGameOver(data);
        this.client.onLog = (msg) => this.addLog(msg);
        this.client.onConnectionChange = (connected) => this.updateConnectionStatus(connected);

        // 노드 클릭 핸들러
        this.renderer.onNodeClick = (nodeId) => this.handleNodeClick(nodeId);

        // 유닛 선택 버튼
        document.querySelectorAll('.unit-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('.unit-btn').forEach(b => b.classList.remove('selected'));
                btn.classList.add('selected');
                this.selectedUnits = parseInt(btn.dataset.count);
                this.updateMoveButton();
            });
        });

        // 이동 버튼
        document.getElementById('btn-move').addEventListener('click', () => {
            this.executeMove();
        });

        // 조우 결정 버튼
        document.getElementById('btn-advance').addEventListener('click', () => {
            this.resolveEncounter('Advance');
        });
        document.getElementById('btn-retreat').addEventListener('click', () => {
            this.resolveEncounter('Retreat');
        });

        // 로그인
        const nameInput = document.getElementById('player-name');
        const findMatchBtn = document.getElementById('btn-find-match');

        nameInput.addEventListener('input', () => {
            findMatchBtn.disabled = !nameInput.value.trim();
        });

        findMatchBtn.addEventListener('click', () => {
            this.startMatchmaking();
        });

        // 새 매치
        document.getElementById('btn-new-match').addEventListener('click', () => {
            location.reload();
        });
    }

    async startMatchmaking() {
        const playerName = document.getElementById('player-name').value.trim();
        if (!playerName) return;

        const statusEl = document.getElementById('match-status');
        statusEl.textContent = '매치 찾는 중...';

        try {
            const match = await this.client.findMatch(playerName);

            document.getElementById('login-screen').classList.remove('active');
            document.getElementById('game-screen').classList.add('active');
            document.getElementById('my-side').textContent =
                match.playerSide === 'A' ? '🔵 Player A' : '🔴 Player B';

            this.renderer.initialize(match.playerSide);
            this.client.connect();
        } catch (error) {
            statusEl.textContent = '매칭 실패: ' + error.message;
            setTimeout(() => { statusEl.textContent = ''; }, 3000);
        }
    }

    handleStateUpdate(state) {
        // 1. 낙관적 UI 업데이트 목록 보정 (이미 서버에 도달했거나 3초가 지난 경우 제거)
        const now = Date.now();
        this.optimisticMoves = this.optimisticMoves.filter(optMove => {
            const confirmed = state.myPendingMoves && state.myPendingMoves.some(
                m => m.fromNodeId === optMove.fromNodeId &&
                     m.toNodeId === optMove.toNodeId &&
                     m.unitCount === optMove.unitCount
            );
            return !confirmed && (now - optMove.timestamp < 3000);
        });

        // 2. 서버 상태 복사 및 낙관적 데이터 합성
        const localState = JSON.parse(JSON.stringify(state));

        // 3. 낙관적 이동 사항을 로컬 상태에 반영
        this.optimisticMoves.forEach(optMove => {
            localState.myRemainingUnits = Math.max(0, localState.myRemainingUnits - optMove.unitCount);

            const fromNode = localState.nodes.find(n => n.id === optMove.fromNodeId);
            if (fromNode && fromNode.myUnits) {
                fromNode.myUnits.mobile = Math.max(0, fromNode.myUnits.mobile - optMove.unitCount);
                fromNode.myUnits.total = Math.max(0, fromNode.myUnits.total - optMove.unitCount);
            }

            if (!localState.myPendingMoves) {
                localState.myPendingMoves = [];
            }
            localState.myPendingMoves.push(optMove);
        });

        localState.isMyPlanningComplete = (localState.myRemainingUnits <= 0) || 
            (localState.nodes && localState.nodes.reduce((sum, n) => sum + (n.myUnits?.mobile || 0), 0) <= 0);

        this.stateView = state; 
        this.isMyPlanningComplete = localState.isMyPlanningComplete;
        this.renderer.updateState(localState); 

        // 라운드 정보 업데이트
        const maxRounds = localState.maxRounds || 20;
        document.getElementById('round-info').textContent = `라운드 ${localState.currentRound} / ${maxRounds}`;
        document.getElementById('phase-info').textContent =
            localState.phase === 'Planning' ? '📋 계획 단계' : '⚡ 해소 단계';

        // 타이머 상태 관리 및 카운트다운 시작/종료
        const phaseChanged = this.lastPhase !== localState.phase;
        const roundChanged = this.lastRound !== localState.currentRound;

        this.lastPhase = localState.phase;
        this.lastRound = localState.currentRound;

        if (localState.phase === 'Planning') {
            if (roundChanged || phaseChanged || !this.timerInterval) {
                this.startCountdown(30);
            } else {
                this.updateTimerDisplay();
            }
        } else {
            this.stopCountdown();
            const timerEl = document.getElementById('timer-display');
            if (timerEl) {
                timerEl.textContent = '⏱️ 해소 단계 진행 중...';
                timerEl.style.color = '#a78bfa'; // Purple/Violet
                timerEl.classList.remove('urgent');
            }
        }

        // 점수 업데이트
        if (localState.scores) {
            document.getElementById('score-a').textContent = `A: ${localState.scores.A || 0}`;
            document.getElementById('score-b').textContent = `B: ${localState.scores.B || 0}`;
        }

        // 이동 가능 유닛 수
        const remaining = localState.myRemainingUnits || 0;
        document.getElementById('available-units').textContent = remaining;

        // 내 Planning 완료 상태
        if (localState.isMyPlanningComplete) {
            document.getElementById('phase-info').textContent = '✅ 명령 완료 (상대 기다리는 중)';
        }

        // 유닛 선택 버튼의 활성화/비활성화 처리
        document.querySelectorAll('.unit-btn').forEach(btn => {
            const btnCount = parseInt(btn.dataset.count);
            if (btnCount > remaining) {
                btn.disabled = true;
                btn.classList.remove('selected');
                btn.style.opacity = '0.3';
                btn.style.cursor = 'not-allowed';
            } else {
                btn.disabled = false;
                btn.style.opacity = '1';
                btn.style.cursor = 'pointer';
            }
        });

        if (this.selectedUnits > remaining) {
            this.selectedUnits = 0;
            document.querySelectorAll('.unit-btn').forEach(b => b.classList.remove('selected'));
            this.updateMoveButton();
        }

        // 예약된 이동 목록 표시
        const pendingMovesEl = document.getElementById('pending-moves');
        if (pendingMovesEl) {
            pendingMovesEl.innerHTML = '';
            if (localState.myPendingMoves && localState.myPendingMoves.length > 0) {
                const header = document.createElement('div');
                header.style.fontWeight = 'bold';
                header.style.margin = '10px 0 5px 0';
                header.style.color = '#38bdf8';
                header.style.fontSize = '0.95em';
                header.textContent = '📍 현재 라운드 이동 예약 목록';
                pendingMovesEl.appendChild(header);

                localState.myPendingMoves.forEach(move => {
                    const moveItem = document.createElement('div');
                    moveItem.style.display = 'flex';
                    moveItem.style.justifyContent = 'space-between';
                    moveItem.style.alignItems = 'center';
                    moveItem.style.padding = '6px 10px';
                    moveItem.style.margin = '5px 0';
                    if (move.isOptimistic) {
                        moveItem.style.backgroundColor = 'rgba(96, 165, 250, 0.08)';
                        moveItem.style.border = '1px dashed rgba(96, 165, 250, 0.3)';
                    } else {
                        moveItem.style.backgroundColor = 'rgba(56, 189, 248, 0.08)';
                        moveItem.style.border = '1px solid rgba(56, 189, 248, 0.15)';
                    }
                    moveItem.style.borderRadius = '6px';
                    moveItem.style.fontSize = '0.85em';

                    const fromName = GameRenderer.NODE_POSITIONS[move.fromNodeId]?.name || `노드 ${move.fromNodeId}`;
                    const toName = GameRenderer.NODE_POSITIONS[move.toNodeId]?.name || `노드 ${move.toNodeId}`;

                    moveItem.innerHTML = `
                        <span style="${move.isOptimistic ? 'opacity: 0.7;' : ''}">${fromName} ➔ ${toName}</span>
                        <span style="font-weight: bold; color: ${move.isOptimistic ? '#60a5fa' : '#38bdf8'};">
                            ${move.unitCount}기 ${move.isOptimistic ? '<span style="font-size:0.8em; font-weight:normal; opacity:0.8;">(전송중...)</span>' : ''}
                        </span>
                    `;
                    pendingMovesEl.appendChild(moveItem);
                });
            }
        }

        // 미결정 조우 하이라이트
        if (localState.undecidedEncounterEdgeIds && localState.undecidedEncounterEdgeIds.length > 0) {
            this.highlightUndecidedEncounters(localState.undecidedEncounterEdgeIds);
        }
    }

    handleNodeClick(nodeId) {
        if (!this.stateView || this.stateView.phase !== 'Planning') return;

        // 이미 Planning 완료면 무시
        if (this.stateView.isMyPlanningComplete) return;

        if (!this.selectedFrom) {
            // 출발지 선택
            const node = this.stateView.nodes.find(n => n.id === nodeId);
            if (!node) return;
            const canSelect = node.isOwnedByMe || (node.ownership?.toLowerCase() === 'contested' && (node.myUnits?.mobile || 0) > 0);
            if (!canSelect) return;

            // 이미 이번 라운드에 출발지 노드에서 보내기로 한 예약을 감안하여 남은 유닛 수 계산
            const alreadyCommitted = this.stateView.myPendingMoves
                ? this.stateView.myPendingMoves.filter(m => m.fromNodeId === nodeId).reduce((sum, m) => sum + m.unitCount, 0)
                : 0;
            const availableOnNode = (node.myUnits?.mobile || 0) - alreadyCommitted;
            if (availableOnNode <= 0) return; // 출발 노드에 사용 가능한 유닛이 없음

            this.selectedFrom = nodeId;
            this.renderer.selectNode(nodeId);
            document.getElementById('selected-from').textContent =
                GameRenderer.NODE_POSITIONS[nodeId].name;
            document.getElementById('selected-to').textContent = '-';

            // 유닛 선택 버튼 업데이트 (출발지의 모바일 유닛 수와 내 전체 남은 유닛 수 중 최소값 기준으로 비활성화)
            const remaining = this.stateView.myRemainingUnits || 0;
            const maxSelectable = Math.Min(remaining, availableOnNode);

            document.querySelectorAll('.unit-btn').forEach(btn => {
                const btnCount = parseInt(btn.dataset.count);
                if (btnCount > maxSelectable) {
                    btn.disabled = true;
                    btn.classList.remove('selected');
                    btn.style.opacity = '0.3';
                    btn.style.cursor = 'not-allowed';
                } else {
                    btn.disabled = false;
                    btn.style.opacity = '1';
                    btn.style.cursor = 'pointer';
                }
            });

            if (this.selectedUnits > maxSelectable) {
                this.selectedUnits = 0;
                document.querySelectorAll('.unit-btn').forEach(b => b.classList.remove('selected'));
            }

            this.updateMoveButton();
        } else if (this.selectedFrom === nodeId) {
            // 선택 취소
            this.selectedFrom = null;
            this.renderer.selectNode(null);
            this.renderer.resetHighlight();
            document.getElementById('selected-from').textContent = '-';
            document.getElementById('selected-to').textContent = '-';

            // 유닛 선택 버튼 원상복구 (전체 남은 유닛 기준)
            const remaining = this.stateView.myRemainingUnits || 0;
            document.querySelectorAll('.unit-btn').forEach(btn => {
                const btnCount = parseInt(btn.dataset.count);
                if (btnCount > remaining) {
                    btn.disabled = true;
                    btn.classList.remove('selected');
                    btn.style.opacity = '0.3';
                    btn.style.cursor = 'not-allowed';
                } else {
                    btn.disabled = false;
                    btn.style.opacity = '1';
                    btn.style.cursor = 'pointer';
                }
            });

            if (this.selectedUnits > remaining) {
                this.selectedUnits = 0;
                document.querySelectorAll('.unit-btn').forEach(b => b.classList.remove('selected'));
            }

            this.updateMoveButton();
        } else {
            // 목적지 선택 → 이동 실행
            document.getElementById('selected-to').textContent =
                GameRenderer.NODE_POSITIONS[nodeId].name;

            if (this.selectedUnits > 0) {
                this.executeMoveTo(nodeId);
            }
        }
    }

    updateMoveButton() {
        const btn = document.getElementById('btn-move');
        btn.disabled = !this.selectedFrom || this.selectedUnits <= 0;
    }

    executeMoveTo(toNode) {
        if (!this.selectedFrom || this.selectedUnits <= 0) return;

        const fromNode = this.selectedFrom;
        const count = this.selectedUnits;

        // 낙관적 UI 정보 기록
        this.optimisticMoves.push({
            fromNodeId: fromNode,
            toNodeId: toNode,
            unitCount: count,
            isOptimistic: true,
            timestamp: Date.now()
        });

        // 서버 전송
        this.client.moveUnits(fromNode, toNode, count);

        // 즉시 로컬 갱신하여 UI 반영
        if (this.stateView) {
            this.handleStateUpdate(this.stateView);
        }

        // UI 초기화
        this.selectedFrom = null;
        this.selectedUnits = 0;
        this.renderer.selectNode(null);
        this.renderer.resetHighlight();
        document.querySelectorAll('.unit-btn').forEach(b => b.classList.remove('selected'));
        document.getElementById('selected-from').textContent = '-';
        document.getElementById('selected-to').textContent = '-';
        document.getElementById('btn-move').disabled = true;
    }

    executeMove() {
        // 버튼 클릭으로는 실행 안 함 (노드 클릭으로 목적지 선택)
    }

    handleEncounter(data) {
        this.encounterData = data;
        document.getElementById('encounter-section').style.display = 'block';
        document.getElementById('encounter-info').innerHTML = `
            <p>간선: ${data.fromNode}-${data.toNode}</p>
            <p>A측 유닛: ${data.participantA.unitCount}기</p>
            <p>B측 유닛: ${data.participantB.unitCount}기</p>
        `;
    }

    resolveEncounter(decision) {
        if (!this.encounterData) return;

        this.client.resolveEncounter(
            this.encounterData.fromNode,
            this.encounterData.toNode,
            decision
        );

        document.getElementById('encounter-section').style.display = 'none';
        this.encounterData = null;
    }

    highlightUndecidedEncounters(edgeIds) {
        document.getElementById('encounter-section').style.display = 'block';
    }

    handleGameOver(data) {
        this.stopCountdown();
        const timerEl = document.getElementById('timer-display');
        if (timerEl) {
            timerEl.textContent = '⏱️ 게임 종료';
            timerEl.style.color = '#9ca3af'; // Gray
            timerEl.classList.remove('urgent');
        }

        const overlay = document.getElementById('gameover-overlay');
        overlay.style.display = 'flex';

        if (data.winner) {
            const isWinner = data.winner === this.client.playerSide;
            document.getElementById('gameover-title').textContent =
                isWinner ? '🎉 승리!' : '😢 패배';
            document.getElementById('gameover-result').textContent =
                `승자: ${data.winner}측`;
        } else {
            document.getElementById('gameover-title').textContent = '🤝 무승부';
        }
    }

    startCountdown(seconds) {
        this.stopCountdown();
        this.timeRemaining = seconds;
        this.updateTimerDisplay();

        this.timerInterval = setInterval(() => {
            this.timeRemaining--;
            if (this.timeRemaining <= 0) {
                this.stopCountdown();
                const timerEl = document.getElementById('timer-display');
                if (timerEl) {
                    timerEl.textContent = '⏱️ 시간 초과 (대기 중)';
                    timerEl.style.color = '#f87171'; // Red
                    timerEl.classList.remove('urgent');
                }
            } else {
                this.updateTimerDisplay();
            }
        }, 1000);
    }

    stopCountdown() {
        if (this.timerInterval) {
            clearInterval(this.timerInterval);
            this.timerInterval = null;
        }
    }

    updateTimerDisplay() {
        const timerEl = document.getElementById('timer-display');
        if (!timerEl) return;

        if (this.isMyPlanningComplete) {
            timerEl.textContent = `⏱️ ${this.timeRemaining}초 (대기 중)`;
            timerEl.style.color = '#34d399'; // Green
            timerEl.classList.remove('urgent');
            return;
        }

        if (this.timeRemaining <= 5) {
            timerEl.textContent = `⏱️ ${this.timeRemaining}초 남음 - 서두르세요!`;
            timerEl.style.color = '#ef4444'; // Red
            timerEl.classList.add('urgent');
        } else {
            timerEl.textContent = `⏱️ ${this.timeRemaining}초 남음`;
            timerEl.style.color = '#fbbf24'; // Yellow/Orange
            timerEl.classList.remove('urgent');
        }
    }

    addLog(message) {
        const log = document.getElementById('game-log');
        const entry = document.createElement('div');
        entry.className = 'log-entry';
        
        // 메시지 텍스트에 따른 직관적인 테마 색상 부여
        if (message.includes('❌ 오류') || message.includes('오류:')) {
            entry.style.color = '#ef4444';
            entry.style.fontWeight = 'bold';
        } else if (message.includes('🎮 게임 시작') || message.includes('🏆 게임 종료')) {
            entry.style.color = '#fbbf24';
            entry.style.fontWeight = 'bold';
        } else if (message.includes('🚀')) {
            entry.style.color = '#38bdf8';
        } else if (message.includes('✅') || message.includes('도착')) {
            entry.style.color = '#34d399';
        } else if (message.includes('⚡') || message.includes('조우')) {
            entry.style.color = '#f472b6';
            entry.style.fontWeight = 'bold';
        } else if (message.includes('📍 라운드')) {
            entry.style.color = '#a78bfa';
            entry.style.fontWeight = 'bold';
        } else if (message.includes('서버 연결됨') || message.includes('연결됨')) {
            entry.style.color = '#10b981';
        } else if (message.includes('연결 끊김')) {
            entry.style.color = '#f87171';
        }

        entry.textContent = new Date().toLocaleTimeString() + ' ' + message;
        log.insertBefore(entry, log.firstChild);

        // 최대 50개 유지
        while (log.children.length > 50) {
            log.removeChild(log.lastChild);
        }
    }

    updateConnectionStatus(connected) {
        const status = document.getElementById('connection-status');
        status.textContent = connected ? '🟢 연결됨' : '🔴 연결 끊김';
        status.style.color = connected ? '#4CAF50' : '#f44336';
    }
}

// 페이지 로드 시 초기화
document.addEventListener('DOMContentLoaded', () => {
    const ui = new GameUI();
    ui.initialize();
    window.gameUI = ui; // 디버깅용
});