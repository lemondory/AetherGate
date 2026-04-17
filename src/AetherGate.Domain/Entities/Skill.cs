using AetherGate.Domain.Enums;
using AetherGate.Domain.ValueObjects;

namespace AetherGate.Domain.Entities;

public record SkillDefinition(
    int Id,
    string Name,
    SkillType Type,
    int Damage,
    float Range,
    int MpCost,
    int CooldownMs,
    int BuffDurationMs = 0,
    int BuffAttackBonus = 0);

public static class SkillCatalog
{
    public static readonly SkillDefinition SlashAttack = new(
        Id: 1, Name: "슬래시", Type: SkillType.SingleAttack,
        Damage: 30, Range: 3f, MpCost: 10, CooldownMs: 2000);

    public static readonly SkillDefinition Whirlwind = new(
        Id: 2, Name: "선풍", Type: SkillType.AreaAttack,
        Damage: 20, Range: 5f, MpCost: 20, CooldownMs: 5000);

    public static readonly SkillDefinition BattleCry = new(
        Id: 3, Name: "전투함성", Type: SkillType.Buff,
        Damage: 0, Range: 0f, MpCost: 15, CooldownMs: 10000,
        BuffDurationMs: 8000, BuffAttackBonus: 15);
}
