using AetherGate.Domain.ValueObjects;

namespace AetherGate.Domain.Messages.Commands;

// ─── Player Commands ──────────────────────────────────────────────────
public sealed record MoveCommand(string PlayerId, Position Destination);
public sealed record UseSkillCommand(string PlayerId, int SkillId, string? TargetId, Position? TargetPosition);
public sealed record PickupItemCommand(string PlayerId, string DropId);
public sealed record UseItemCommand(string PlayerId, int ItemId);
public sealed record EnhanceItemCommand(string PlayerId, int ItemId);
public sealed record BuyItemCommand(string PlayerId, int ItemId, int Quantity);
public sealed record SendChatCommand(string PlayerId, string Message, string? WhisperTargetId = null);

// ─── Tick ─────────────────────────────────────────────────────────────
public sealed record Tick(long TickNumber, long ElapsedMs);

// ─── Zone Lifecycle ───────────────────────────────────────────────────
public sealed record DungeonClearRequest(string ZoneInstanceId);
public sealed record DungeonFailRequest(string ZoneInstanceId);
