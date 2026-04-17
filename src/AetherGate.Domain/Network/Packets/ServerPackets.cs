using MessagePack;

namespace AetherGate.Domain.Network.Packets;

// ─── Server → Client 패킷 DTO ─────────────────────────────────────────

[MessagePackObject]
public sealed class LoginResultPacket
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string? PlayerId { get; set; }
    [Key(2)] public string? Token { get; set; }
    [Key(3)] public string? ErrorMessage { get; set; }
}

[MessagePackObject]
public sealed class PlayerEnteredPacket
{
    [Key(0)] public string PlayerId { get; set; } = string.Empty;
    [Key(1)] public float X { get; set; }
    [Key(2)] public float Y { get; set; }
}

[MessagePackObject]
public sealed class PlayerLeftPacket
{
    [Key(0)] public string PlayerId { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class PlayerMovedPacket
{
    [Key(0)] public string PlayerId { get; set; } = string.Empty;
    [Key(1)] public float X { get; set; }
    [Key(2)] public float Y { get; set; }
}

[MessagePackObject]
public sealed class MonsterSpawnedPacket
{
    [Key(0)] public string MonsterId { get; set; } = string.Empty;
    [Key(1)] public string TemplateId { get; set; } = string.Empty;
    [Key(2)] public float X { get; set; }
    [Key(3)] public float Y { get; set; }
}

[MessagePackObject]
public sealed class MonsterMovedPacket
{
    [Key(0)] public string MonsterId { get; set; } = string.Empty;
    [Key(1)] public float X { get; set; }
    [Key(2)] public float Y { get; set; }
}

[MessagePackObject]
public sealed class MonsterStatePacket
{
    [Key(0)] public string MonsterId { get; set; } = string.Empty;
    [Key(1)] public int State { get; set; }  // MonsterState enum 값
}

[MessagePackObject]
public sealed class MonsterDiedPacket
{
    [Key(0)] public string MonsterId { get; set; } = string.Empty;
    [Key(1)] public string? KillerPlayerId { get; set; }
}

[MessagePackObject]
public sealed class CombatDamagePacket
{
    [Key(0)] public string AttackerId { get; set; } = string.Empty;
    [Key(1)] public string TargetId { get; set; } = string.Empty;
    [Key(2)] public int Damage { get; set; }
    [Key(3)] public bool IsCritical { get; set; }
}

[MessagePackObject]
public sealed class SkillUsedPacket
{
    [Key(0)] public string CasterId { get; set; } = string.Empty;
    [Key(1)] public int SkillId { get; set; }
    [Key(2)] public string? TargetId { get; set; }
    [Key(3)] public float? TargetX { get; set; }
    [Key(4)] public float? TargetY { get; set; }
}

[MessagePackObject]
public sealed class BuffAppliedPacket
{
    [Key(0)] public string PlayerId { get; set; } = string.Empty;
    [Key(1)] public int SkillId { get; set; }
    [Key(2)] public int DurationMs { get; set; }
}

[MessagePackObject]
public sealed class ItemDroppedPacket
{
    [Key(0)] public string DropId { get; set; } = string.Empty;
    [Key(1)] public int ItemId { get; set; }
    [Key(2)] public int Quantity { get; set; }
    [Key(3)] public float X { get; set; }
    [Key(4)] public float Y { get; set; }
}

[MessagePackObject]
public sealed class ItemPickedUpPacket
{
    [Key(0)] public string PlayerId { get; set; } = string.Empty;
    [Key(1)] public string DropId { get; set; } = string.Empty;
    [Key(2)] public int ItemId { get; set; }
    [Key(3)] public int Quantity { get; set; }
}

[MessagePackObject]
public sealed class ItemEnhancedPacket
{
    [Key(0)] public string PlayerId { get; set; } = string.Empty;
    [Key(1)] public int ItemId { get; set; }
    [Key(2)] public int NewLevel { get; set; }
    [Key(3)] public bool Success { get; set; }
}

[MessagePackObject]
public sealed class GoldChangedPacket
{
    [Key(0)] public int Delta { get; set; }
    [Key(1)] public int NewTotal { get; set; }
}

[MessagePackObject]
public sealed class ChatMessagePacket
{
    [Key(0)] public string SenderName { get; set; } = string.Empty;
    [Key(1)] public string Message { get; set; } = string.Empty;
    [Key(2)] public bool IsWhisper { get; set; }
}

[MessagePackObject]
public sealed class DungeonCreatedPacket
{
    [Key(0)] public string ZoneInstanceId { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class DungeonResultPacket
{
    [Key(0)] public string ZoneInstanceId { get; set; } = string.Empty;
    [Key(1)] public bool IsCleared { get; set; }
}

[MessagePackObject]
public sealed class ErrorPacket
{
    [Key(0)] public string Message { get; set; } = string.Empty;
    [Key(1)] public int Code { get; set; }
}
