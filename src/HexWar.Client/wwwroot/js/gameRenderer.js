// wwwroot/js/gameRenderer.js

/**
 * SVG 기반 게임 보드 렌더러
 */
class GameRenderer {
    constructor(svgElement) {
        this.svg = svgElement;
        this.nodes = {};
        this.edges = {};
        this.selectedNode = null;
        this.selectedUnits = 0;
        this.playerSide = null;
        this.onNodeClick = null;
        this.onUnitSelect = null;
    }

    /**
     * 노드 위치 정의 (6개 노드)
     */
    static NODE_POSITIONS = {
        1: { x: 100, y: 200, name: '서부 전초기지' },
        2: { x: 250, y: 80, name: '북부 고지' },
        3: { x: 400, y: 200, name: '동부 교차로' },
        4: { x: 250, y: 400, name: '남부 통로' },
        5: { x: 500, y: 300, name: '동부 전초기지' },
        6: { x: 300, y: 250, name: '중앙 사령부' }
    };

    /**
     * 간선 정의 (14개)
     */
    static EDGES = [
        [1, 2], [1, 4], [1, 5], [1, 6],
        [2, 3], [2, 6],
        [3, 4], [3, 5], [3, 6],
        [4, 5], [4, 6],
        [5, 6]
    ];

    /**
     * 게임 보드 초기화
     */
    initialize(playerSide) {
        this.playerSide = playerSide;
        this.svg.innerHTML = '';

        // 간선 그리기
        GameRenderer.EDGES.forEach(([from, to]) => {
            this.drawEdge(from, to);
        });

        // 노드 그리기
        Object.entries(GameRenderer.NODE_POSITIONS).forEach(([id, pos]) => {
            this.drawNode(parseInt(id), pos);
        });
    }

    drawEdge(from, to) {
        const posFrom = GameRenderer.NODE_POSITIONS[from];
        const posTo = GameRenderer.NODE_POSITIONS[to];
        const key = `${from}-${to}`;

        const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        line.setAttribute('x1', posFrom.x);
        line.setAttribute('y1', posFrom.y);
        line.setAttribute('x2', posTo.x);
        line.setAttribute('y2', posTo.y);
        line.setAttribute('class', 'edge-line');
        line.setAttribute('data-edge', key);

        // 거리 라벨
        const midX = (posFrom.x + posTo.x) / 2;
        const midY = (posFrom.y + posTo.y) / 2;
        const label = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        label.setAttribute('x', midX);
        label.setAttribute('y', midY - 8);
        label.setAttribute('class', 'edge-label');

        this.svg.appendChild(line);
        this.svg.appendChild(label);

        this.edges[key] = { line, label };
    }

    drawNode(id, pos) {
        const group = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        group.setAttribute('data-node', id);

        // 원
        const circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
        circle.setAttribute('cx', pos.x);
        circle.setAttribute('cy', pos.y);
        circle.setAttribute('r', 30);
        circle.setAttribute('class', 'node-circle neutral');
        circle.addEventListener('click', () => this.handleNodeClick(id));

        // 이름
        const label = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        label.setAttribute('x', pos.x);
        label.setAttribute('y', pos.y + 4);
        label.setAttribute('class', 'node-label');
        label.textContent = pos.name.substring(0, 2);

        // 유닛 수
        const units = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        units.setAttribute('x', pos.x);
        units.setAttribute('y', pos.y - 40);
        units.setAttribute('class', 'node-units');
        units.textContent = '0';

        group.appendChild(circle);
        group.appendChild(label);
        group.appendChild(units);
        this.svg.appendChild(group);

        this.nodes[id] = { group, circle, label, units };
    }

    handleNodeClick(nodeId) {
        if (this.onNodeClick) {
            this.onNodeClick(nodeId);
        }
    }

    /**
     * 게임 상태로 보드 업데이트
     */
    updateState(stateView) {
        if (!stateView || !stateView.nodes) return;

        // 노드 업데이트
        stateView.nodes.forEach(node => {
            const nodeEl = this.nodes[node.id];
            if (!nodeEl) return;

            // 소유권에 따른 색상
            nodeEl.circle.setAttribute('class', `node-circle ${node.ownership.toLowerCase()}`);
            if (node.isHeadquarters) nodeEl.circle.classList.add('headquarters');
            if (node.isSupplyLine) nodeEl.circle.classList.add('supplyline');

            // 아군/적군 진지 명확히 구분 (.mine / .opponent)
            if (node.isOwnedByMe) {
                nodeEl.circle.classList.add('mine');
            } else if (node.ownership !== 'Neutral' && node.ownership !== 'Contested') {
                nodeEl.circle.classList.add('opponent');
            }

            // 유닛 수 표시
            const myUnits = node.myUnits ? node.myUnits.total : 0;
            const enemyUnits = node.enemyUnits ? node.enemyUnits.totalCount : 0;

            if (node.isHeadquarters) {
                nodeEl.units.textContent = `내 ${myUnits}`;
            } else {
                nodeEl.units.textContent = `${myUnits}/${enemyUnits}`;
            }

            // 내 노드 강조
            if (node.isOwnedByMe) {
                nodeEl.circle.style.strokeDasharray = '5,2';
            } else {
                nodeEl.circle.style.strokeDasharray = 'none';
            }
        });

        // 계획 단계 예약된 이동 간선 하이라이트 및 화살표 표시
        Object.values(this.edges).forEach(edgeEl => {
            edgeEl.line.classList.remove('pending-move', 'optimistic');
            if (edgeEl.pendingLabel) {
                edgeEl.pendingLabel.remove();
                edgeEl.pendingLabel = null;
            }
        });

        if (stateView.myPendingMoves) {
            stateView.myPendingMoves.forEach(move => {
                const key = this.edges[`${move.fromNodeId}-${move.toNodeId}`]
                    ? `${move.fromNodeId}-${move.toNodeId}`
                    : `${move.toNodeId}-${move.fromNodeId}`;
                const edgeEl = this.edges[key];
                if (edgeEl) {
                    edgeEl.line.classList.add('pending-move');
                    if (move.isOptimistic) {
                        edgeEl.line.classList.add('optimistic');
                    }

                    // 간선 중앙에 유닛 수 및 이동 방향 텍스트 렌더링
                    const posStart = GameRenderer.NODE_POSITIONS[move.fromNodeId];
                    const posEnd = GameRenderer.NODE_POSITIONS[move.toNodeId];
                    const midX = (posStart.x + posEnd.x) / 2;
                    const midY = (posStart.y + posEnd.y) / 2;

                    // 이동 방향 계산
                    const dx = posEnd.x - posStart.x;
                    const dy = posEnd.y - posStart.y;
                    let arrow = '➔';
                    if (Math.abs(dx) > Math.abs(dy)) {
                        arrow = dx > 0 ? '➔' : '◀';
                    } else {
                        arrow = dy > 0 ? '▼' : '▲';
                    }

                    const pendingLabel = document.createElementNS('http://www.w3.org/2000/svg', 'text');
                    pendingLabel.setAttribute('x', midX);
                    pendingLabel.setAttribute('y', midY + 12);
                    pendingLabel.setAttribute('class', 'pending-edge-label');
                    if (move.isOptimistic) {
                        pendingLabel.classList.add('optimistic');
                    }
                    pendingLabel.textContent = `${arrow} ${move.unitCount}기`;

                    this.svg.appendChild(pendingLabel);
                    edgeEl.pendingLabel = pendingLabel;
                }
            });
        }

        // 간선 업데이트
        if (stateView.edges) {
            stateView.edges.forEach(edge => {
                const key = `${edge.fromNodeId}-${edge.toNodeId}`;
                const edgeEl = this.edges[key];
                if (!edgeEl) return;

                edgeEl.line.classList.remove('has-units', 'encounter');

                if (edge.hasEncounter) {
                    edgeEl.line.classList.add('encounter');
                } else if (edge.hasMyUnitsTraveling) {
                    edgeEl.line.classList.add('has-units');
                }
            });
        }

        // 이동 가능 유닛 수 업데이트
        if (this.onUnitSelect) {
            this.onUnitSelect(stateView.myRemainingUnits || 0);
        }
    }

    selectNode(nodeId) {
        // 이전 선택 해제
        if (this.selectedNode && this.nodes[this.selectedNode]) {
            this.nodes[this.selectedNode].circle.classList.remove('selected');
        }
        this.selectedNode = nodeId;
        if (nodeId && this.nodes[nodeId]) {
            this.nodes[nodeId].circle.classList.add('selected');
        }
    }

    highlightNeighbors(nodeId) {
        // 모든 노드 opacity 낮추기
        Object.values(this.nodes).forEach(n => {
            n.circle.style.opacity = '0.4';
        });

        // 선택된 노드와 이웃만 강조
        if (nodeId && this.nodes[nodeId]) {
            this.nodes[nodeId].circle.style.opacity = '1';
        }
    }

    resetHighlight() {
        Object.values(this.nodes).forEach(n => {
            n.circle.style.opacity = '1';
        });
    }
}