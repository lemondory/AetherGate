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

    // 위치 캐시 — 몬스터 감지 스캔 / AoE 범위 판별에 사용
    private readonly Dictionary<string, Position> _playerPositions  = new();
    private readonly Dictionary<string, Position> _monsterPositions = new();

    private IActorRef _chatActor     = ActorRefs.Nobody;
    private IActorRef _tickScheduler = ActorRefs.Nobody;

    // 리스폰 타이머 — PostStop에서 일괄 취소
    private readonly List<ICancelable> _respawnTimers = new();

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
            _playerPositions[evt.PlayerId] = evt.To;
            BroadcastToSessionsExcept(evt, evt.PlayerId);
        });
        Receive<MonsterSpawned>(evt => BroadcastToSessions(evt));
        Receive<MonsterMoved>(evt =>
        {
            _monsterPositions[evt.MonsterId] = evt.Position; // 위치 캐시 갱신
            BroadcastToSessions(evt);
        });
        Receive<MonsterStateChanged>(evt => BroadcastToSessions(evt));
        Receive<MonsterDied>(evt         => HandleMonsterDied(evt));

        // CombatDamage: 전체 세션에 브로드캐스트(시각 피드백) + 대상 Actor에 전달(HP 감소)
        Receive<CombatDamage>(HandleCombatDamage);

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

        // ─── 운영자 전체 공지 ─────────────────────────────────────────
        Receive<AdminBroadcastRequest>(req =>
            BroadcastToSessions(new AdminNotification(req.Message)));

        // ─── AoE 스킬 범위 피해 처리 ──────────────────────────────────
        // PlayerActor가 AreaAttack 시 ZoneActor에 위임 → 범위 내 몬스터에게 CombatDamage 전송
        Receive<PlayerActor.AreaSkillRequest>(HandleAreaSkill);

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
        _playerPositions[req.PlayerId] = req.SpawnPosition;

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
    // 전투 피해 — 세션 브로드캐스트 + 대상 Actor HP 감소
    // ─────────────────────────────────────────────────────────────────
    private void HandleCombatDamage(CombatDamage evt)
    {
        // 1. 전체 세션에 브로드캐스트 (데미지 숫자, 이펙트 표시)
        BroadcastToSessions(evt);

        // 2. 대상 Actor에 전달하여 실제 HP 감소 처리
        if (_monsters.TryGetValue(evt.TargetId, out var monster))
            monster.Tell(evt);
        else if (_players.TryGetValue(evt.TargetId, out var player))
            player.Tell(evt);
    }

    // ─────────────────────────────────────────────────────────────────
    // 몬스터 사망 처리
    // ─────────────────────────────────────────────────────────────────
    private void HandleMonsterDied(MonsterDied evt)
    {
        _monsterPositions.Remove(evt.MonsterId);
        BroadcastToSessions(evt);

        if (!_config.IsInstance)
        {
            // 필드맵: 30초 후 리스폰 (ICancelable로 관리 — PostStop에서 취소)
            _respawnTimers.Add(Context.System.Scheduler.ScheduleTellOnceCancelable(
                TimeSpan.FromSeconds(30), Self, new RespawnMonster(evt.MonsterId), Self));
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
    // AoE 스킬 — 캐시된 몬스터 위치로 범위 내 타겟 판별 후 CombatDamage 전송
    // ─────────────────────────────────────────────────────────────────
    private void HandleAreaSkill(PlayerActor.AreaSkillRequest req)
    {
        var rng = new Random();
        bool crit = rng.NextSingle() < 0.10f;
        int baseDamage = req.Skill.Damage + (crit ? req.Skill.Damage / 2 : 0);

        foreach (var (monsterId, monsterPos) in _monsterPositions)
        {
            if (monsterPos.DistanceTo(req.Center) <= req.Skill.Range)
            {
                // HandleCombatDamage를 통해 세션 브로드캐스트 + MonsterActor HP 감소
                Self.Tell(new CombatDamage(req.CasterId, monsterId, baseDamage, crit));
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // 몬스터 감지 스캔 — 감지 범위 내 플레이어를 찾아 MonsterActor에 응답
    // ─────────────────────────────────────────────────────────────────
    private void HandleScanForPlayers(MonsterActor.ScanForPlayers req)
    {
        if (!_monsters.TryGetValue(req.MonsterId, out var monsterActor)) return;
        if (_playerPositions.Count == 0) return;

        var nearest = _playerPositions
            .Select(kv => (PlayerId: kv.Key, Pos: kv.Value, Dist: kv.Value.DistanceTo(req.Position)))
            .Where(x => x.Dist <= req.Range)
            .OrderBy(x => x.Dist)
            .FirstOrDefault();

        if (nearest.PlayerId is not null)
            monsterActor.Tell(new MonsterActor.PlayerDetected(nearest.PlayerId, nearest.Pos));
    }

    // ─────────────────────────────────────────────────────────────────
    // 라우팅 헬퍼
    // ─────────────────────────────────────────────────────────────────
    private void ForwardToPlayer(string playerId, object msg)
    {
        if (_players.TryGetValue(playerId, out var actor))
            actor.Tell(msg);
    }

    private void BroadcastToSessions(object msg)
    {
        foreach (var session in _sessions.Values)
            session.Tell(msg);
    }

    private void BroadcastToSessionsExcept(object msg, string excludePlayerId)
    {
        foreach (var (playerId, session) in _sessions)
            if (playerId != excludePlayerId)
                session.Tell(msg);
    }

    private void SendToSession(string playerId, object msg)
    {
        if (_sessions.TryGetValue(playerId, out var session))
            session.Tell(msg);
    }

    protected override void PostStop()
    {
        foreach (var timer in _respawnTimers)
            timer.Cancel();
    }

    public sealed record RespawnMonster(string MonsterId);
}
