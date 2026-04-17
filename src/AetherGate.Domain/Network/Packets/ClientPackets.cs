using MessagePack;

namespace AetherGate.Domain.Network.Packets;

// ─── Client → Server 패킷 DTO ─────────────────────────────────────────
// MessagePack 직렬화 대상 (key는 순서 기반 — 필드 추가 시 끝에만 붙일 것)

[MessagePackObject]
public sealed class LoginPacket
{
    [Key(0)] public string Username { get; set; } = string.Empty;
    [Key(1)] public string PasswordHash { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class MovePacket
{
    [Key(0)] public float X { get; set; }
    [Key(1)] public float Y { get; set; }
}

[MessagePackObject]
public sealed class UseSkillPacket
{
    [Key(0)] public int SkillId { get; set; }
    [Key(1)] public string? TargetId { get; set; }
    [Key(2)] public float? TargetX { get; set; }
    [Key(3)] public float? TargetY { get; set; }
}

[MessagePackObject]
public sealed class PickupItemPacket
{
    [Key(0)] public string DropId { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class UseItemPacket
{
    [Key(0)] public int ItemId { get; set; }
}

[MessagePackObject]
public sealed class EnhanceItemPacket
{
    [Key(0)] public int ItemId { get; set; }
}

[MessagePackObject]
public sealed class BuyItemPacket
{
    [Key(0)] public int ItemId { get; set; }
    [Key(1)] public int Quantity { get; set; }
}

[MessagePackObject]
public sealed class ZoneChatPacket
{
    [Key(0)] public string Message { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class WhisperPacket
{
    [Key(0)] public string TargetName { get; set; } = string.Empty;
    [Key(1)] public string Message { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class EnterDungeonPacket
{
    [Key(0)] public string DungeonTemplateId { get; set; } = string.Empty;
}
