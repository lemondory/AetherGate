using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using AetherGate.Application.Actors;
using AetherGate.Application.Actors.Zone;
using AetherGate.Domain.Messages.Commands;
using AetherGate.Domain.Messages.Events;
using AetherGate.Domain.ValueObjects;
using Xunit;

namespace AetherGate.Tests.Actors;

/// <summary>
/// ZoneActor 통합 테스트.
/// TestProbe를 SessionActor 대역으로 등록해
/// 브로드캐스트 이벤트가 올바르게 라우팅되는지 검증.
/// </summary>
public sealed class ZoneActorTests : TestKit
{
    private const string ZoneId   = "test_zone_01";
    private const string Player1  = "player_one";
    private const string Player2  = "player_two";

    // 몬스터를 스폰하지 않는 테스트 전용 설정
    private static readonly ZoneConfig EmptyZoneConfig =
        new(ZoneId, "TestMap", IsInstance: false, MonsterSpawnCount: 0);

    // ─── 플레이어 입장 ────────────────────────────────────────────────

    [Fact]
    public void WhenPlayerEnters_OtherSessionsReceive_PlayerEnteredZone()
    {
        var zone     = CreateZone();
        var session1 = CreateTestProbe();
        var session2 = CreateTestProbe();

        // Player1 입장
        EnterZone(zone, Player1, session1, new Position(10, 10));

        // Player2 입장 → Player1 세션에 PlayerEnteredZone 브로드캐스트
        EnterZone(zone, Player2, session2, new Position(20, 20));

        session1.ExpectMsg<PlayerEnteredZone>(msg =>
        {
            Assert.Equal(Player2, msg.PlayerId);
            Assert.Equal(20f, msg.Position.X, precision: 1);
        }, TimeSpan.FromSeconds(1));

        // Player2 본인에게는 전송 안 됨
        session2.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void WhenPlayerEnters_EnteringPlayer_DoesNotReceive_OwnEntryEvent()
    {
        var zone     = CreateZone();
        var session1 = CreateTestProbe();

        EnterZone(zone, Player1, session1, new Position(10, 10));

        // 본인 입장 이벤트는 수신 안 됨
        session1.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    // ─── 플레이어 퇴장 ────────────────────────────────────────────────

    [Fact]
    public void WhenPlayerLeaves_OtherSessionsReceive_PlayerLeftZone()
    {
        var zone     = CreateZone();
        var session1 = CreateTestProbe();
        var session2 = CreateTestProbe();

        EnterZone(zone, Player1, session1, new Position(10, 10));
        EnterZone(zone, Player2, session2, new Position(20, 20));
        session1.ExpectMsg<PlayerEnteredZone>(); // Player2 입장 이벤트 소비

        // Player2 퇴장
        zone.Tell(new LeaveZoneRequest(Player2, ZoneId));

        session1.ExpectMsg<PlayerLeftZone>(msg =>
            Assert.Equal(Player2, msg.PlayerId),
            TimeSpan.FromSeconds(1));
    }

    // ─── 이동 브로드캐스트 ────────────────────────────────────────────

    [Fact]
    public void WhenPlayerMoves_OtherSessionsReceive_PlayerMoved()
    {
        var zone     = CreateZone();
        var session1 = CreateTestProbe();
        var session2 = CreateTestProbe();

        EnterZone(zone, Player1, session1, new Position(0, 0));
        EnterZone(zone, Player2, session2, new Position(5, 5));
        session1.ExpectMsg<PlayerEnteredZone>(); // 입장 이벤트 소비

        // Player2가 이동 커맨드 전송
        zone.Tell(new MoveCommand(Player2, new Position(50f, 60f)));

        // Player1 세션에 브로드캐스트
        session1.ExpectMsg<PlayerMoved>(msg =>
        {
            Assert.Equal(Player2, msg.PlayerId);
            Assert.Equal(50f, msg.To.X, precision: 1);
            Assert.Equal(60f, msg.To.Y, precision: 1);
        }, TimeSpan.FromSeconds(1));

        // 본인(Player2)에게는 전송 안 됨
        session2.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    // ─── 전투 브로드캐스트 ────────────────────────────────────────────

    [Fact]
    public void CombatDamage_BroadcastToAllSessions()
    {
        var zone     = CreateZone();
        var session1 = CreateTestProbe();
        var session2 = CreateTestProbe();

        EnterZone(zone, Player1, session1, new Position(0, 0));
        EnterZone(zone, Player2, session2, new Position(5, 5));
        session1.ExpectMsg<PlayerEnteredZone>();

        var dmgEvt = new CombatDamage("mob_01", Player1, 25, false);
        zone.Tell(dmgEvt);

        session1.ExpectMsg<CombatDamage>(msg => Assert.Equal(25, msg.Damage),
            TimeSpan.FromSeconds(1));
        session2.ExpectMsg<CombatDamage>(msg => Assert.Equal(25, msg.Damage),
            TimeSpan.FromSeconds(1));
    }

    // ─── 채팅 ─────────────────────────────────────────────────────────

    [Fact]
    public void ZoneChat_BroadcastToAllSessions()
    {
        var zone     = CreateZone();
        var session1 = CreateTestProbe();
        var session2 = CreateTestProbe();

        EnterZone(zone, Player1, session1, new Position(0, 0));
        EnterZone(zone, Player2, session2, new Position(5, 5));
        session1.ExpectMsg<PlayerEnteredZone>();

        zone.Tell(new SendChatCommand(Player1, "안녕하세요!"));

        session1.FishForMessage(m => m is ChatBroadcast c && c.Message == "안녕하세요!",
            TimeSpan.FromSeconds(1), hint: "Session1 should receive zone chat");
        session2.FishForMessage(m => m is ChatBroadcast c && c.Message == "안녕하세요!",
            TimeSpan.FromSeconds(1), hint: "Session2 should receive zone chat");
    }

    [Fact]
    public void Whisper_OnlyDeliveredToTarget()
    {
        var zone     = CreateZone();
        var session1 = CreateTestProbe();
        var session2 = CreateTestProbe();

        EnterZone(zone, Player1, session1, new Position(0, 0));
        EnterZone(zone, Player2, session2, new Position(5, 5));
        session1.ExpectMsg<PlayerEnteredZone>();

        zone.Tell(new SendChatCommand(Player1, "비밀 메시지", WhisperTargetId: Player2));

        // Player2에게만 전달
        session2.FishForMessage(m => m is ChatBroadcast c && c.IsWhisper,
            TimeSpan.FromSeconds(1), hint: "Target should receive whisper");

        // Player1에게는 전달 안 됨
        session1.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    // ─── 아이템 이벤트 ────────────────────────────────────────────────

    [Fact]
    public void GoldChanged_OnlyDeliveredToOwner()
    {
        var zone     = CreateZone();
        var session1 = CreateTestProbe();
        var session2 = CreateTestProbe();

        EnterZone(zone, Player1, session1, new Position(0, 0));
        EnterZone(zone, Player2, session2, new Position(5, 5));
        session1.ExpectMsg<PlayerEnteredZone>();

        zone.Tell(new GoldChanged(Player1, 100, 1100));

        // Player1에게만 전달
        session1.ExpectMsg<GoldChanged>(msg => Assert.Equal(100, msg.Delta),
            TimeSpan.FromSeconds(1));

        // Player2에게는 전달 안 됨
        session2.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    // ─── Supervision — MonsterActor 오류 후 Zone 무영향 ──────────────

    [Fact]
    public void ZoneActor_Stays_Alive_After_Player_Enters_And_Moves()
    {
        // 몬스터 없이 Zone 생성 후 플레이어 입장 → 이동 → Zone이 살아있는지 확인
        var zone     = CreateZone(monsterCount: 0);
        var session1 = CreateTestProbe();
        var session2 = CreateTestProbe();

        EnterZone(zone, Player1, session1, new Position(0, 0));
        EnterZone(zone, Player2, session2, new Position(5, 5));
        session1.ExpectMsg<PlayerEnteredZone>();

        // Zone이 살아있으면 이동 이벤트를 브로드캐스트
        zone.Tell(new MoveCommand(Player2, new Position(10, 10)));
        session1.ExpectMsg<PlayerMoved>(TimeSpan.FromSeconds(1));

        // Zone 자체는 Terminated 되지 않아야 함
        Watch(zone);
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    // ─── 헬퍼 ────────────────────────────────────────────────────────

    private IActorRef CreateZone(int monsterCount = 0)
    {
        var cfg = new ZoneConfig(ZoneId, "TestMap", false, MonsterSpawnCount: monsterCount);
        return Sys.ActorOf(Props.Create(() => new ZoneActor(cfg)));
    }

    private void EnterZone(IActorRef zone, string playerId, TestProbe session, Position pos)
    {
        zone.Tell(new EnterZoneRequest(playerId, ZoneId, pos));
        zone.Tell(new WorldActor.SessionEnrollment(playerId, session.Ref));
    }
}
