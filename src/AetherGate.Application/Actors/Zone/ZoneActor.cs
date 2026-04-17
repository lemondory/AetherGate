using Akka.Actor;
using Akka.Event;
using AetherGate.Domain.Messages.Commands;
using AetherGate.Domain.Messages.Events;
using AetherGate.Domain.ValueObjects;

namespace AetherGate.Application.Actors.Zone;

/// <summary>
/// 하나의 맵 인스턴스를 담당.
///
/// 두 종류의 수신자를 관리:
/// - _players  : PlayerActor (게임 로직 — 이동/전투/AI 메시지 수신)
/// - _sessions : SessionActor (네트워크 — 직렬화 후 TCP 클라이언트로 전달)
///
/// 브로드캐스트는 항상 _sessions 기준으로 수행.
///
/// Supervision Strategy:
/// - MonsterActor 예외 → Restart (게임 무영향)
/// - ChatActor 예외   → Restart
/// - PlayerActor 예외 → Stop   (세션 레벨에서 처리)
/// </summary>
public sealed class ZoneActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ZoneConfig _config;

    // 게임 로직 Actor
    private readonly Dictionary<string, IActorRef> _players  = new();
    private readonly Dictionary<string, IActorRef> _monsters = new();

    // 네트워크 브로드캐스트 대상 (playerId → SessionActor)
    private readonly Dictionary<string, IActorRef> _sessions = new();

    // 몬스터 감지 스캔용 플레이어 위치 캐시 (playerId → Position)
    private readonly Dictionary<string, Position> _playerPositions = new();

    private IActorRef _chatActor    = ActorRefs.Nobody;
    private IActorRef _tickScheduler = ActorRefs.Nobody;

    public ZoneActor(ZoneConfig config)
    {
        _config = config;

        // ─── 입/퇴장 ──────────────────────────────────────────────────
        Receive<EnterZoneRequest>(HandlePlayerEnter);
        Receive<LeaveZoneRequest>(HandlePlayerLeave);

        // ─── 클라이언트 → 게임 로직 라우팅 ──────────────────────────
        Receive<MoveCommand>(msg        => ForwardToPlayer(msg.PlayerId, msg));
        Receive<UseSkillCommand>(msg    => ForwardToPlayer(msg.PlayerId, msg));
        Receive<PickupItemCommand>(msg  => ForwardToPlayer(msg.PlayerId, msg));
        Receive<UseItemCommand>(msg     => ForwardToPlayer(msg.PlayerId, msg));
        Receive<EnhanceItemCommand>(msg => ForwardToPlayer(msg.PlayerId, msg));
        Receive<BuyItemCommand>(msg     => ForwardToPlayer(msg.PlayerId, msg));
        Receive<SendChatCommand>(msg    => _chatActor.Tell(msg));

        // ─── 게임 이벤트 → 클라이언트 브로드캐스트 ──────────────────
        Receive<PlayerMoved>(evt =>
        {
            _playerPositions[evt.PlayerId] = evt.To;  // 위치 캐시 갱신
            BroadcastToSessionsExcept(evt, evt.PlayerId);
        });
        Receive<MonsterSpawned>(evt      => BroadcastToSessions(evt));
        Receive<MonsterMoved>(evt        => BroadcastToSessions(evt));
        Receive<MonsterStateChanged>(evt => BroadcastToSessions(evt));
        Receive<MonsterDied>(evt         => HandleMonsterDied(evt));
        Receive<CombatDamage>(evt        => BroadcastToSessions(evt));
        Receive<SkillUsed>(evt           => BroadcastToSessions(evt));
        Receive<BuffApplied>(evt         => BroadcastToSessions(evt));
        Receive<ItemDropped>(evt         => BroadcastToSessions(evt));
        Receive<ItemPickedUp>(evt        => BroadcastToSessionsExcept(evt, evt.PlayerId));
        Receive<ItemEnhanced>(evt        => SendToSession(evt.PlayerId, evt));
        Receive<GoldChanged>(evt         => SendToSession(evt.PlayerId, evt));
        Receive<ChatBroadcast>(evt           => HandleChatBroadcast(evt));
        Receive<ChatActor.WhisperMessage>(msg => SendToSession(msg.TargetId, msg.Broadcast));

        // ─── 던전 라이프사이클 ────────────────────────────────────────
        Receive<DungeonClearRequest>(req =>
        {
            BroadcastToSessions(new DungeonCleared(req.ZoneInstanceId));
            Context.Parent.Tell(req);
        });
        Receive<DungeonFailRequest>(req =>
        {
            BroadcastToSessions(new DungeonFailed(req.ZoneInstanceId));
            Context.Parent.Tell(req);
        });

        // ─── 몬스터 감지 스캔 요청 ────────────────────────────────────
        // MonsterActor가 Patrol/Return Tick마다 전송 → Zone이 플레이어 위치 계산 후 응답
        Receive<MonsterActor.ScanForPlayers>(HandleScanForPlayers);

        // ─── 세션 등록 (WorldActor가 EnterZoneRequest 직후 전달) ──────
        Receive<WorldActor.SessionEnrollment>(msg =>
            _sessions[msg.PlayerId] = msg.Session);

        // ─── 리스폰 / 종료 ────────────────────────────────────────────
        Receive<RespawnMonster>(HandleRespawn);
        Receive<WorldActor.Terminate>(_ => Context.Stop(Self));

        // ─── Tick ─────────────────────────────────────────────────────
        Receive<Tick>(HandleTick);
    }

    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(ex =>
        {
            return ex switch
            {
                ActorInitializationException => Directive.Stop,
                ActorKilledException         => Directive.Stop,
                _ when Sender.Path.Name.StartsWith("monster-") => Directive.Restart,
                _ when Sender.Path.Name == "chat"              => Directive.Restart,
                _ => Directive.Stop
            };
        });
    }

    protected override void PreStart()
    {
        _chatActor = Context.ActorOf(
            Props.Create(() => new ChatActor(_config.ZoneId)), "chat");
        _tickScheduler = Context.ActorOf(
            Props.Create(() => new TickSchedulerActor(Self, _config.TickIntervalMs)), "tick");

        SpawnMonsters();
        _log.Info("[Zone:{0}] Started (map={1}, instance={2})",
            _config.ZoneId, _config.MapId, _config.IsInstance);
    }

    // ─────────────────────────────────────────────────────────────────
    // 플레이어 입/퇴장
    // ─────────────────────────────────────────────────────────────────
    private void HandlePlayerEnter(EnterZoneRequest req)
    {
        if (_players.ContainsKey(req.PlayerId)) return;

        var playerActor = Context.ActorOf(
            Props.Create(() => new PlayerActor(req.PlayerId, req.SpawnPosition, Self)),
            $"player-{req.PlayerId}");
        _players[req.PlayerId] = playerActor;
        _playerPositions[req.PlayerId] = req.SpawnPosition;  // 초기 위치 등록

        BroadcastToSessionsExcept(
            new PlayerEnteredZone(req.PlayerId, req.SpawnPosition), req.PlayerId);

        _log.Info("[Zone:{0}] Player entered: {1}", _config.ZoneId, req.PlayerId);
    }

    private void HandlePlayerLeave(LeaveZoneRequest req)
    {
        _sessions.Remove(req.PlayerId);
        _playerPositions.Remove(req.PlayerId);

        if (_players.Remove(req.PlayerId, out var playerActor))
        {
            Context.Stop(playerActor);
            BroadcastToSessions(new PlayerLeftZone(req.PlayerId));
            _log.Info("[Zone:{0}] Player left: {1}", _config.ZoneId, req.PlayerId);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tick — 몬스터 AI 루프
    // ─────────────────────────────────────────────────────────────────
    private void HandleTick(Tick tick)
    {
        foreach (var monster in _monsters.Values)
            monster.Tell(tick);
    }

    // ─────────────────────────────────────────────────────────────────
    // 몬스터 사망 처리
    // ─────────────────────────────────────────────────────────────────
    private void HandleMonsterDied(MonsterDied evt)
    {
        BroadcastToSessions(evt);

        if (!_config.IsInstance)
        {
            // 필드맵: 30초 후 리스폰
            Context.System.Scheduler.ScheduleTellOnce(
                TimeSpan.FromSeconds(30), Self, new RespawnMonster(evt.MonsterId), Self);
        }
        else
        {
            _monsters.Remove(evt.MonsterId);

            // 던전: 몬스터 전멸 시 클리어
            if (_monsters.Count == 0)
                Self.Tell(new DungeonClearRequest(_config.ZoneId));
        }
    }

    private void HandleRespawn(RespawnMonster msg)
    {
        if (!_monsters.ContainsKey(msg.MonsterId)) return;

        var rng = new Random();
        var spawnPos = new Position(rng.Next(10, 200), rng.Next(10, 200));
        var newActor = Context.ActorOf(
            Props.Create(() => new MonsterActor(msg.MonsterId, "Goblin", spawnPos, Self)),
            $"monster-{msg.MonsterId}");
        _monsters[msg.MonsterId] = newActor;
    }

    // ─────────────────────────────────────────────────────────────────
    // 채팅 — 존 브로드캐스트 / 귓속말 분기
    // ─────────────────────────────────────────────────────────────────
    private void HandleChatBroadcast(ChatBroadcast evt)
    {
        if (evt.IsWhisper && evt.WhisperTargetId is not null)
            SendToSession(evt.WhisperTargetId, evt);
        else
            BroadcastToSessions(evt);
    }

    // ─────────────────────────────────────────────────────────────────
    // 몬스터 스폰
    // ─────────────────────────────────────────────────────────────────
    private void SpawnMonsters()
    {
        var rng = new Random();
        for (int i = 0; i < _config.MonsterSpawnCount; i++)
        {
            string monsterId = $"mob_{_config.ZoneId}_{i}";
            var spawnPos = new Position(rng.Next(10, 200), rng.Next(10, 200));
            var actor = Context.ActorOf(
                Props.Create(() => new MonsterActor(monsterId, "Goblin", spawnPos, Self)),
                $"monster-{monsterId}");
            _monsters[monsterId] = actor;
        }
        _log.Info("[Zone:{0}] Spawned {1} monsters", _config.ZoneId, _config.MonsterSpawnCount);
    }

    // ─────────────────────────────────────────────────────────────────
    // 라우팅 헬퍼
    // ─────────────────────────────────────────────────────────────────
    private void ForwardToPlayer(string playerId, object msg)
    {
        if (_players.TryGetValue(playerId, out var actor))
            actor.Tell(msg);
    }

    // 모든 SessionActor에게 전달
    private void BroadcastToSessions(object msg)
    {
        foreach (var session in _sessions.Values)
            session.Tell(msg);
    }

    // 특정 플레이어를 제외하고 전달
    private void BroadcastToSessionsExcept(object msg, string excludePlayerId)
    {
        foreach (var (playerId, session) in _sessions)
            if (playerId != excludePlayerId)
                session.Tell(msg);
    }

    // 특정 플레이어 세션에만 전달
    private void SendToSession(string playerId, object msg)
    {
        if (_sessions.TryGetValue(playerId, out var session))
            session.Tell(msg);
    }

    // ─────────────────────────────────────────────────────────────────
    // 몬스터 감지 스캔 — 감지 범위 내 플레이어를 찾아 MonsterActor에 응답
    // ─────────────────────────────────────────────────────────────────
    private void HandleScanForPlayers(MonsterActor.ScanForPlayers req)
    {
        if (!_monsters.TryGetValue(req.MonsterId, out var monsterActor)) return;

        // PlayerActor가 없으면 스캔 불필요
        if (_players.Count == 0) return;

        // 가장 가까운 플레이어 탐색 (PlayerActor의 위치를 직접 알 수 없으므로
        // ZoneActor가 별도로 플레이어 위치 캐시를 유지하는 구조 사용)
        if (!_playerPositions.Any()) return;

        var nearest = _playerPositions
            .Select(kv => (PlayerId: kv.Key, Pos: kv.Value, Dist: kv.Value.DistanceTo(req.Position)))
            .Where(x => x.Dist <= req.Range)
            .OrderBy(x => x.Dist)
            .FirstOrDefault();

        if (nearest.PlayerId is not null)
        {
            monsterActor.Tell(new MonsterActor.PlayerDetected(nearest.PlayerId, nearest.Pos));
        }
    }

    public sealed record RespawnMonster(string MonsterId);
}
