using AetherGate.Domain.Entities;
using AetherGate.Domain.Enums;
using Xunit;

namespace AetherGate.Tests.Items;

/// <summary>
/// InventoryItem 도메인 단위 테스트.
/// Actor 없이 순수 도메인 로직 검증 (강화 성공률, 소비, 스택).
/// </summary>
public sealed class InventoryItemTests
{
    private static readonly ItemDefinition WeaponDef =
        new(Id: 10, Name: "롱소드", Type: ItemType.Equipment, MaxStack: 1, SellPrice: 500);

    private static readonly ItemDefinition PotionDef =
        new(Id: 1, Name: "체력포션", Type: ItemType.Consumable, MaxStack: 99, SellPrice: 50);

    // ─── 강화 성공률 테이블 ───────────────────────────────────────────

    [Theory]
    [InlineData(0,  0.90f)]
    [InlineData(1,  0.90f)]
    [InlineData(2,  0.90f)]
    [InlineData(3,  0.90f)]
    [InlineData(4,  0.70f)]
    [InlineData(5,  0.70f)]
    [InlineData(6,  0.70f)]
    [InlineData(7,  0.50f)]
    [InlineData(8,  0.50f)]
    [InlineData(9,  0.50f)]
    [InlineData(10, 0.30f)]
    [InlineData(11, 0.30f)]
    public void GetEnhanceSuccessRate_ReturnsCorrectRate(int currentLevel, float expected)
    {
        float rate = InventoryItem.GetEnhanceSuccessRate(currentLevel);
        Assert.Equal(expected, rate, precision: 2);
    }

    // ─── 강화 성공 (확정 성공 Random 사용) ───────────────────────────

    [Fact]
    public void TryEnhance_WithAlwaysSucceedRng_IncrementsLevel()
    {
        var item = new InventoryItem(WeaponDef, 1);
        var rng  = new AlwaysSucceedRandom();

        bool success = item.TryEnhance(rng);

        Assert.True(success);
        Assert.Equal(1, item.EnhanceLevel);
    }

    [Fact]
    public void TryEnhance_WithAlwaysFailRng_DoesNotIncrementLevel()
    {
        var item = new InventoryItem(WeaponDef, 1);
        var rng  = new AlwaysFailRandom();

        bool success = item.TryEnhance(rng);

        Assert.False(success);
        Assert.Equal(0, item.EnhanceLevel);
    }

    [Fact]
    public void TryEnhance_MultipleSuccess_IncrementsLevelEachTime()
    {
        var item = new InventoryItem(WeaponDef, 1);
        var rng  = new AlwaysSucceedRandom();

        for (int i = 0; i < 5; i++)
            item.TryEnhance(rng);

        Assert.Equal(5, item.EnhanceLevel);
    }

    // ─── 소비 아이템 ─────────────────────────────────────────────────

    [Fact]
    public void TryConsume_SufficientQuantity_DecreasesCountAndReturnsTrue()
    {
        var item = new InventoryItem(PotionDef, 5);

        bool result = item.TryConsume(2);

        Assert.True(result);
        Assert.Equal(3, item.Quantity);
    }

    [Fact]
    public void TryConsume_InsufficientQuantity_ReturnsFalse_AndDoesNotChange()
    {
        var item = new InventoryItem(PotionDef, 1);

        bool result = item.TryConsume(2);

        Assert.False(result);
        Assert.Equal(1, item.Quantity); // 변하지 않아야 함
    }

    [Fact]
    public void TryConsume_ExactQuantity_LeavesZero()
    {
        var item = new InventoryItem(PotionDef, 3);

        bool result = item.TryConsume(3);

        Assert.True(result);
        Assert.Equal(0, item.Quantity);
    }

    // ─── 스택 추가 ────────────────────────────────────────────────────

    [Fact]
    public void Add_IncreasesQuantity()
    {
        var item = new InventoryItem(PotionDef, 10);
        item.Add(5);
        Assert.Equal(15, item.Quantity);
    }

    // ─── 헬퍼 — 결과 확정 Random ─────────────────────────────────────

    /// <summary>항상 0f 반환 → 성공률 조건(rng.NextSingle() &lt; rate) 항상 참</summary>
    private sealed class AlwaysSucceedRandom : Random
    {
        public override float NextSingle() => 0f;
    }

    /// <summary>항상 1f 반환 → 성공률 조건 항상 거짓</summary>
    private sealed class AlwaysFailRandom : Random
    {
        public override float NextSingle() => 1f;
    }
}
