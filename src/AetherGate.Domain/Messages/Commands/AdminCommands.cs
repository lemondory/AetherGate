namespace AetherGate.Domain.Messages.Commands;

/// <summary>
/// 운영자 명령 타입 — FastAPI Admin API → Redis Pub/Sub → .NET 서버
/// </summary>
public enum AdminCommandType
{
    Kick,       // 특정 플레이어 강제 퇴장
    Broadcast,  // 전체 공지
}

/// <summary>Redis에서 역직렬화된 운영자 명령 — AdminBridgeActor → GameServerActor</summary>
public sealed record AdminCommand(AdminCommandType Type, string? PlayerId, string? Message);

/// <summary>WorldActor: 특정 플레이어 강제 퇴장</summary>
public sealed record KickPlayerRequest(string PlayerId);

/// <summary>WorldActor → 모든 ZoneActor: 운영자 전체 공지</summary>
public sealed record AdminBroadcastRequest(string Message);
