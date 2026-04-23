using AetherGate.Domain.Enums;

namespace AetherGate.Domain.Entities;

public record ItemDefinition(
    int Id,
    string Name,
    ItemType Type,
    int MaxStack,
    int SellPrice);

public class InventoryItem
{
    public ItemDefinition Definition { get; }
    public int Quantity { get; private set; }
    public int EnhanceLevel { get; private set; }

    public InventoryItem(ItemDefinition definition, int quantity)
    {
        Definition = definition;
        Quantity = quantity;
        EnhanceLevel = 0;
    }

    public bool TryConsume(int amount)
    {
        if (Quantity < amount) return false;
        Quantity -= amount;
        return true;
    }

    public void Add(int amount) => Quantity += amount;

    // 강화 성공률 테이블: +0~+3 90%, +4~+6 70%, +7~+9 50%, +10 30%
    public static float GetEnhanceSuccessRate(int currentLevel) => currentLevel switch
    {
        < 4  => 0.90f,
        < 7  => 0.70f,
        < 10 => 0.50f,
        _    => 0.30f
    };

    public bool TryEnhance(Random rng)
    {
        float rate = GetEnhanceSuccessRate(EnhanceLevel);
        if (rng.NextSingle() < rate)
        {
            EnhanceLevel++;
            return true;
        }
        return false;
    }
}
