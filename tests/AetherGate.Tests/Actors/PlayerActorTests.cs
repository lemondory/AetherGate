using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using AetherGate.Application.Actors.Zone;
using AetherGate.Domain.Entities;
using AetherGate.Domain.Messages.Commands;
using AetherGate.Domain.Messages.Events;
using AetherGate.Domain.ValueObjects;
using Xunit;

namespace AetherGate.Tests.Actors;

/// <summary>
/// PlayerActor 단위 테스트.
/// TestProbe를 ZoneActor 대역으로 등록해
/// 이동/스킬/아이템/골드 이벤트가 올바르게 전달되는지 검증.
/// </summary>
public sealed class PlayerActorTests : TestKit
{
    private const string PlayerId = "test_player_01";
    private static readonly Position SpawnPos = new(50f, 50f);

    // ─── 이동 ─────────────────────────────────────────────────────────

    [Fact]
    public void Move_SendsPlayerMoved_ToZone_WithCorrectPositions()
    {
        var zone   = CreateTestProbe();
        var player = CreatePlayer(zone);

        var dest = new Position(100f, 200f);
        player.Tell(new MoveCommand(PlayerId, dest));

        zone.ExpectMsg<PlayerMoved>(msg =>
        {
            Assert.Equal(PlayerId, msg.PlayerId);
            Assert.Equal(SpawnPos.X, msg.From.X, precision: 1);
            Assert.Equal(dest.X,    msg.To.X,   precision: 1);
            Assert.Equal(dest.Y,    msg.To.Y,   precision: 1);
        }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ConsecutiveMoves_UpdateFromPosition()
    {
        var zone   = CreateTestProbe();
        var player = CreatePlayer(zone);

        var first  = new Position(10f, 10f);
        var second = new Position(20f, 20f);

        player.Tell(new MoveCommand(PlayerId, first));
        zone.ExpectMsg<PlayerMoved>();

        player.Tell(new MoveCommand(PlayerId, second));
        zone.ExpectMsg<PlayerMoved>(msg =>
        {
            Assert.Equal(first.X,  msg.From.X, precision: 1);
            Assert.Equal(second.X, msg.To.X,   precision: 1);
        }, TimeSpan.FromSeconds(1));
    }

    // ─── 스킬 ─────────────────────────────────────────────────────────

    [Fact]
    public void UseSkill_SingleAttack_SendsSkillUsed_And_CombatDamage()
    {
        var zone   = CreateTestProbe();
        var player = CreatePlayer(zone);

        player.Tell(new UseSkillCommand(PlayerId, SkillId: 1, TargetId: "mob_01", TargetPosition: null));

        zone.FishForMessage(m => m is SkillUsed s && s.SkillId == 1,
            TimeSpan.FromSeconds(1), hint: "Expected SkillUsed");

        zone.FishForMessage(m => m is CombatDamage d && d.TargetId == "mob_01" && d.Damage > 0,
            TimeSpan.FromSeconds(1), hint: "Expected CombatDamage > 0");
    }

    [Fact]
    public void UseSkill_Buff_SendsBuffApplied()
    {
        var zone   = CreateTestProbe();
        var player = CreatePlayer(zone);

        // BattleCry (skillId=3) — SkillType.Buff
        player.Tell(new UseSkillCommand(PlayerId, SkillId: 3, TargetId: null, TargetPosition: null));

        zone.FishForMessage(m => m is BuffApplied b && b.SkillId == 3,
            TimeSpan.FromSeconds(1), hint: "Expected BuffApplied for BattleCry");
    }

    [Fact]
    public void UseSkill_WhileOnCooldown_DoesNotSendCombatDamage_Twice()
    {
        var zone   = CreateTestProbe();
        var player = CreatePlayer(zone);

        // 슬래시 쿨다운: 2000ms → 즉시 두 번 사용하면 두 번째는 무시
        player.Tell(new UseSkillCommand(PlayerId, SkillId: 1, TargetId: "mob_01", TargetPosition: null));
        zone.FishForMessage(m => m is CombatDamage, TimeSpan.FromSeconds(1));

        player.Tell(new UseSkillCommand(PlayerId, SkillId: 1, TargetId: "mob_01", TargetPosition: null));

        // 두 번째 CombatDamage 는 전송되지 않아야 함
        zone.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void UseSkill_UnknownSkillId_SendsNothing()
    {
        var zone   = CreateTestProbe();
        var player = CreatePlayer(zone);

        player.Tell(new UseSkillCommand(PlayerId, SkillId: 999, TargetId: "mob_01", TargetPosition: null));

        zone.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    // ─── 구매 / 골드 ──────────────────────────────────────────────────

    [Fact]
    public void BuyItem_DeductsGold_SendsGoldChanged_WithNegativeDelta()
    {
        var zone   = CreateTestProbe();
        var player = CreatePlayer(zone);

        // 체력포션 1개 구매 (가격 100골드, 초기 골드 1000)
        player.Tell(new BuyItemCommand(PlayerId, ItemId: 1, Quantity: 1));

        zone.ExpectMsg<GoldChanged>(msg =>
        {
            Assert.Equal(PlayerId, msg.PlayerId);
            Assert.Equal(-100, msg.Delta);
            Assert.Equal(900, msg.NewTotal);
        }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void BuyItem_WithInsufficientGold_SendsNothing()
    {
        var zone   = CreateTestProbe();
        var player = CreatePlayer(zone);

        // 초기 골드 1000 → 10개 구매시 1000골드 필요 → 가능
        // 11개 구매시 1100골드 필요 → 불가
        player.Tell(new BuyItemCommand(PlayerId, ItemId: 1, Quantity: 11));

        zone.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void BuyItem_UnknownItemId_SendsNothing()
    {
        var zone   = CreateTestProbe();
        var player = CreatePlayer(zone);

        player.Tell(new BuyItemCommand(PlayerId, ItemId: 999, Quantity: 1));

        zone.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    // ─── 강화 ─────────────────────────────────────────────────────────

    [Fact]
    public void EnhanceItem_AfterBuying_AlwaysSendsItemEnhanced_Regardless_OfSuccess()
    {
        var zone   = CreateTestProbe();
        var player = CreatePlayer(zone);

        // 아이템 구매
        player.Tell(new BuyItemCommand(PlayerId, ItemId: 1, Quantity: 1));
        zone.ExpectMsg<GoldChanged>();

        // 강화 시도 (+0 → 성공률 90%)
        player.Tell(new EnhanceItemCommand(PlayerId, ItemId: 1));

        // 성공/실패 무관하게 ItemEnhanced 이벤트는 항상 발행
        zone.ExpectMsg<ItemEnhanced>(msg =>
        {
            Assert.Equal(PlayerId, msg.PlayerId);
            Assert.Equal(1, msg.ItemId);
            // NewLevel은 성공이면 1, 실패면 0
            Assert.True(msg.NewLevel >= 0);
        }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void EnhanceItem_NotInInventory_SendsNothing()
    {
        var zone   = CreateTestProbe();
        var player = CreatePlayer(zone);

        // 인벤토리에 없는 아이템
        player.Tell(new EnhanceItemCommand(PlayerId, ItemId: 1));

        zone.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    // ─── 피격 / 사망 ──────────────────────────────────────────────────

    [Fact]
    public void TakeDamage_DoesNotCrash_Actor()
    {
        var zone   = CreateTestProbe();
        var player = CreatePlayer(zone);

        // 충분한 대미지로 HP 0 처리 — Actor는 종료되지 않아야 함
        player.Tell(new CombatDamage("mob_01", PlayerId, 9999, false));

        Watch(player);
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void MonsterDied_ByThisPlayer_SendsGoldChanged()
    {
        var zone   = CreateTestProbe();
        var player = CreatePlayer(zone);

        // 이 플레이어가 처치
        player.Tell(new MonsterDied("mob_01", KillerPlayerId: PlayerId));

        zone.ExpectMsg<GoldChanged>(msg =>
        {
            Assert.Equal(PlayerId, msg.PlayerId);
            Assert.True(msg.Delta > 0, "Gold drop should be positive");
        }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void MonsterDied_ByOtherPlayer_DoesNotSendGoldChanged()
    {
        var zone   = CreateTestProbe();
        var player = CreatePlayer(zone);

        // 다른 플레이어가 처치
        player.Tell(new MonsterDied("mob_01", KillerPlayerId: "other_player"));

        zone.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    // ─── 헬퍼 ────────────────────────────────────────────────────────

    private IActorRef CreatePlayer(TestProbe zone) =>
        Sys.ActorOf(Props.Create(() => new PlayerActor(PlayerId, SpawnPos, zone.Ref)));
}
