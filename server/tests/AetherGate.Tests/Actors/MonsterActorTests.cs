using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using AetherGate.Application.Actors.Zone;
using AetherGate.Domain.Messages.Commands;
using AetherGate.Domain.Messages.Events;
using AetherGate.Domain.ValueObjects;
using Xunit;

namespace AetherGate.Tests.Actors;

/// <summary>
/// MonsterActor FSM 단위 테스트.
/// TestProbe를 ZoneActor 대역으로 사용해 메시지 흐름 검증.
/// </summary>
public sealed class MonsterActorTests : TestKit
{
    private const string MonsterId   = "test_mob_01";
    private const string TemplateId  = "Goblin";
    private static readonly Position SpawnPos = new(100f, 100f);

    // ─── 스폰 ────────────────────────────────────────────────────────

    [Fact]
    public void OnStart_Sends_MonsterSpawned_To_Zone()
    {
        var zone    = CreateTestProbe();
        var monster = CreateMonster(zone);

        zone.ExpectMsg<MonsterSpawned>(msg =>
        {
            Assert.Equal(MonsterId,  msg.MonsterId);
            Assert.Equal(TemplateId, msg.TemplateId);
        });
    }

    // ─── Patrol 상태 ──────────────────────────────────────────────────

    [Fact]
    public void OnTick_InPatrol_Sends_ScanForPlayers()
    {
        var zone    = CreateTestProbe();
        var monster = CreateMonster(zone);
        ConsumePreStartMessages(zone); // MonsterSpawned + MonsterStateChanged(Patrol) 소비

        monster.Tell(new Tick(1, 100));

        zone.FishForMessage(m => m is MonsterActor.ScanForPlayers s && s.MonsterId == MonsterId,
            TimeSpan.FromSeconds(1),
            hint: "Expected ScanForPlayers from patrol tick");
    }

    [Fact]
    public void OnTick_InPatrol_Sends_MonsterMoved()
    {
        var zone    = CreateTestProbe();
        var monster = CreateMonster(zone);
        ConsumePreStartMessages(zone);

        monster.Tell(new Tick(1, 100));

        zone.FishForMessage(m => m is MonsterMoved,
            TimeSpan.FromSeconds(1),
            hint: "Expected MonsterMoved from patrol tick");
    }

    // ─── Detect 상태 전환 ────────────────────────────────────────────

    [Fact]
    public void WhenPlayerDetected_TransitionsTo_DetectState()
    {
        var zone    = CreateTestProbe();
        var monster = CreateMonster(zone);
        ConsumePreStartMessages(zone);

        monster.Tell(new MonsterActor.PlayerDetected("player_01", new Position(105f, 100f)));

        zone.FishForMessage(m => m is MonsterStateChanged s && s.NewState == Domain.Enums.MonsterState.Detect,
            TimeSpan.FromSeconds(1), hint: "Expected Detect state");
    }

    // ─── Chase 상태 전환 ─────────────────────────────────────────────

    [Fact]
    public void AfterDetect_TransitionsTo_ChaseState()
    {
        var zone    = CreateTestProbe();
        var monster = CreateMonster(zone);
        ConsumePreStartMessages(zone);

        monster.Tell(new MonsterActor.PlayerDetected("player_01", new Position(105f, 100f)));
        zone.FishForMessage(m => m is MonsterStateChanged s && s.NewState == Domain.Enums.MonsterState.Detect,
            TimeSpan.FromSeconds(1));

        // 0.5초 Detect 대기 후 Chase 전환
        zone.FishForMessage(m => m is MonsterStateChanged s && s.NewState == Domain.Enums.MonsterState.Chase,
            TimeSpan.FromSeconds(2), hint: "Expected Chase state after Detect delay");
    }

    [Fact]
    public void InChase_OnTick_Sends_MonsterMoved_TowardTarget()
    {
        var zone    = CreateTestProbe();
        var monster = CreateMonster(zone);
        ConsumePreStartMessages(zone);

        var targetPos = new Position(110f, 100f);
        monster.Tell(new MonsterActor.PlayerDetected("player_01", targetPos));
        zone.FishForMessage(m => m is MonsterStateChanged s && s.NewState == Domain.Enums.MonsterState.Chase,
            TimeSpan.FromSeconds(2));

        monster.Tell(new Tick(2, 100));

        zone.FishForMessage(m =>
            m is MonsterMoved mv && mv.Position.X > SpawnPos.X,
            TimeSpan.FromSeconds(1),
            hint: "Expected MonsterMoved toward target (X should increase)");
    }

    // ─── Attack 상태 전환 ────────────────────────────────────────────

    [Fact]
    public void WhenTargetInRange_TransitionsTo_AttackState()
    {
        var zone    = CreateTestProbe();
        var monster = CreateMonster(zone);
        ConsumePreStartMessages(zone);

        // AttackRange=2 이내 위치
        var closePos = new Position(SpawnPos.X + 1f, SpawnPos.Y);
        monster.Tell(new MonsterActor.PlayerDetected("player_01", closePos));
        zone.FishForMessage(m => m is MonsterStateChanged s && s.NewState == Domain.Enums.MonsterState.Chase,
            TimeSpan.FromSeconds(2));

        monster.Tell(new Tick(2, 100));

        zone.FishForMessage(m => m is MonsterStateChanged s && s.NewState == Domain.Enums.MonsterState.Attack,
            TimeSpan.FromSeconds(1), hint: "Expected Attack state when target in range");
    }

    // ─── Return 상태 전환 ────────────────────────────────────────────

    [Fact]
    public void WhenPlayerLeavesZone_InChase_TransitionsTo_Return()
    {
        var zone    = CreateTestProbe();
        var monster = CreateMonster(zone);
        ConsumePreStartMessages(zone);

        monster.Tell(new MonsterActor.PlayerDetected("player_01", new Position(110f, 100f)));
        zone.FishForMessage(m => m is MonsterStateChanged s && s.NewState == Domain.Enums.MonsterState.Chase,
            TimeSpan.FromSeconds(2));

        monster.Tell(new PlayerLeftZone("player_01"));

        zone.FishForMessage(m => m is MonsterStateChanged s && s.NewState == Domain.Enums.MonsterState.Return,
            TimeSpan.FromSeconds(1), hint: "Expected Return state when player leaves");
    }

    // ─── 피격 / 사망 처리 ────────────────────────────────────────────

    [Fact]
    public void WhenDamageExceedsHp_Sends_MonsterDied()
    {
        var zone    = CreateTestProbe();
        var monster = CreateMonster(zone);
        ConsumePreStartMessages(zone);

        monster.Tell(new CombatDamage(MonsterId, MonsterId, 9999, false));

        zone.FishForMessage(m => m is MonsterDied d && d.MonsterId == MonsterId,
            TimeSpan.FromSeconds(1), hint: "Expected MonsterDied");
    }

    [Fact]
    public void WhenDamageExceedsHp_Actor_Stops()
    {
        var zone    = CreateTestProbe();
        var monster = CreateMonster(zone);
        ConsumePreStartMessages(zone);

        Watch(monster);
        monster.Tell(new CombatDamage(MonsterId, MonsterId, 9999, false));

        ExpectTerminated(monster, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void WhenHitInPatrol_TransitionsTo_Chase()
    {
        var zone    = CreateTestProbe();
        var monster = CreateMonster(zone);
        ConsumePreStartMessages(zone);

        // 피격만으로 Chase 전환 (PlayerDetected 없이)
        monster.Tell(new CombatDamage("player_01", MonsterId, 5, false));

        zone.FishForMessage(m => m is MonsterStateChanged s && s.NewState == Domain.Enums.MonsterState.Chase,
            TimeSpan.FromSeconds(1), hint: "Expected Chase state after being hit");
    }

    // ─── 헬퍼 ────────────────────────────────────────────────────────

    private IActorRef CreateMonster(TestProbe zone) =>
        Sys.ActorOf(Props.Create(() =>
            new MonsterActor(MonsterId, TemplateId, SpawnPos, zone.Ref)));

    /// PreStart에서 전송되는 초기 메시지 소비 (MonsterSpawned + MonsterStateChanged(Patrol))
    private static void ConsumePreStartMessages(TestProbe zone)
    {
        zone.ExpectMsg<MonsterSpawned>();
        zone.ExpectMsg<MonsterStateChanged>(msg =>
            Assert.Equal(Domain.Enums.MonsterState.Patrol, msg.NewState));
    }
}
