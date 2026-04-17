namespace AetherGate.Domain.Network;

/// <summary>
/// 패킷 타입 식별자.
/// 0x0xxx : Client → Server
/// 0x8xxx : Server → Client
/// </summary>
public enum PacketId : ushort
{
    // ─── Client → Server ─────────────────────────────────────────────
    Login            = 0x0001,

    Move             = 0x0101,
    UseSkill         = 0x0102,
    PickupItem       = 0x0103,
    UseItem          = 0x0104,
    EnhanceItem      = 0x0105,
    BuyItem          = 0x0106,

    ZoneChat         = 0x0201,
    Whisper          = 0x0202,

    EnterDungeon     = 0x0301,

    // ─── Server → Client ─────────────────────────────────────────────
    LoginResult      = 0x8001,

    PlayerEntered    = 0x8101,
    PlayerLeft       = 0x8102,
    PlayerMoved      = 0x8103,
    MonsterSpawned   = 0x8104,
    MonsterMoved     = 0x8105,
    MonsterState     = 0x8106,
    MonsterDied      = 0x8107,
    CombatDamage     = 0x8108,
    SkillUsed        = 0x8109,
    BuffApplied      = 0x810A,

    ItemDropped      = 0x8201,
    ItemPickedUp     = 0x8202,
    ItemEnhanced     = 0x8203,
    GoldChanged      = 0x8204,

    ChatMessage      = 0x8301,

    DungeonCreated   = 0x8401,
    DungeonResult    = 0x8402,

    Error            = 0x8FFF,
}
