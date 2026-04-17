using AetherGate.Domain.ValueObjects;

namespace AetherGate.Domain.Messages.Commands;

// ─── Gateway / Session Commands ───────────────────────────────────────
public sealed record ClientConnected(string SessionId, System.Net.IPEndPoint RemoteEndPoint);
public sealed record ClientDisconnected(string SessionId);
public sealed record ClientPacketReceived(string SessionId, byte[] Payload);

// ─── Zone Entry ───────────────────────────────────────────────────────
public sealed record EnterZoneRequest(string PlayerId, string ZoneId, Position SpawnPosition);
public sealed record LeaveZoneRequest(string PlayerId, string ZoneId);
public sealed record EnterDungeonRequest(string PlayerId, string DungeonTemplateId);
