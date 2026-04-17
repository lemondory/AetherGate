namespace AetherGate.Domain.ValueObjects;

public record Stats(
    int MaxHp,
    int MaxMp,
    int Attack,
    int Defense,
    int MoveSpeed,
    int AttackRange,
    int DetectRange)
{
    public static Stats Default => new(100, 50, 10, 5, 5, 2, 10);

    public static Stats ForMonster(int level) => new(
        MaxHp: 30 + level * 20,
        MaxMp: 0,
        Attack: 5 + level * 3,
        Defense: 2 + level,
        MoveSpeed: 3,
        AttackRange: 2,
        DetectRange: 8 + level);
}
