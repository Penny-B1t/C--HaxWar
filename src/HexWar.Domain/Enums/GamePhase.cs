namespace HexWar.Domain.Enums;

public enum GamePhase
{
    WatingForPlayers,
    Planning, // 라운드 시작 전 계획 단계
    Resolution, // 명령 실행 및 판정 진행
    GameOver,
}

