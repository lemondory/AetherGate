using Akka.Actor;
using Akka.Event;
using AetherGate.Domain.Messages.Commands;
using AetherGate.Domain.Messages.Events;
using AetherGate.Domain.ValueObjects;

namespace AetherGate.Application.Actors.Zone;

/// <summary>
/// 몬스터 AI FSM — Akka의 Become()으로 상태 전환을 구현.
///
/// 상태 전이:
///   Patrol → Detect(감지 범위 진입) → Chase(추적) → Attack(공격 범위) → Return(타겟 소실)
///   Return → Patrol (귀환 완료)
///
/// 포트폴리오 포인트:
/// - 각 상태별 Become()으로 메시지 핸들러가 교체됨 → 깔끔한 FSM
/// - ZoneActor의 Supervision으로 예외 시 재시작 → 게임 무영향
/// </summary>
public sealed class MonsterActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _zoneActor;

    // ─── 몬스터 기본 정보 ──────────────────────────────────────────────
    public string MonsterId { get; }
    private readonly string _templateId;
    private readonly Position _spawnPosition;
    private readonly Stats _stats;
    private Position _position;
    private int _currentHp;

    // ─── AI 상태 데이터 ────────────────────────────────────────────────
    private string? _targetPlayerId;
    private Position _targetPosition;
    private int _patrolIndex = 0;
    private Position[] _patrolPath;
    private readonly Random _rng = new();
    private ICancelable? _detectTimer;

    // 마지막 공격 시각
    private DateTime _lastAttackTime = DateTime.MinValue;
    private const int AttackCooldownMs = 1500;

    public MonsterActor(string monsterId, string templateId, Position spawnPosition, IActorRef zoneActor)
    {
        MonsterId = monsterId;
        _templateId = templateId;
        _spawnPosition = spawnPosition;
        _position = spawnPosition;
        _zoneActor = zoneActor;
        _stats = Stats.ForMonster(level: 1);
        _currentHp = _stats.MaxHp;

        // 스폰 주변 4방향 순찰 경로
        _patrolPath = new[]
        {
            new Position(spawnPosition.X + 10, spawnPosition.Y),
            new Position(spawnPosition.X + 10, spawnPosition.Y + 10),
            new Position(spawnPosition.X,      spawnPosition.Y + 10),
            spawnPosition
        };

        // 초기 행동 핸들러 등록 (생성자에서는 메시지 전송 금지 — PreStart에서 수행)
        Become(Patrol);
    }

    protected override void PreStart()
    {
        // SpawnedMonster 와 초기 상태 알림을 PreStart에서 전송 (생성자 이후 안전한 시점)
        _zoneActor.Tell(new MonsterSpawned(MonsterId, _templateId, _position));
        _zoneActor.Tell(new MonsterStateChanged(MonsterId, Domain.Enums.MonsterState.Patrol));
    }

    // ─────────────────────────────────────────────────────────────────────
    // STATE: Patrol — 순찰 경로 따라 이동, 감지 범위 내 플레이어 탐색
    // ─────────────────────────────────────────────────────────────────────
    private void BecomePatrol()
    {
        _log.Debug("[Monster:{0}] → Patrol", MonsterId);
        _targetPlayerId = null;
        _zoneActor.Tell(new MonsterStateChanged(MonsterId, Domain.Enums.MonsterState.Patrol));
        Become(Patrol);
    }

    private void Patrol()
    {
        Receive<Tick>(_ => TickPatrol());
        Receive<CombatDamage>(HandleDamageInAnyState);
        Receive<PlayerDetected>(msg => BecomeDetect(msg.PlayerId, msg.Position));
    }

    private void TickPatrol()
    {
        // 순찰 경로 이동
        var target = _patrolPath[_patrolIndex % _patrolPath.Length];
        if (_position.DistanceTo(target) < 0.5f)
            _patrolIndex++;

        _position = _position.MoveToward(target, _stats.MoveSpeed * 0.1f);
        _zoneActor.Tell(new MonsterMoved(MonsterId, _position));

        // ZoneActor에 플레이어 감지 요청 (실제로는 ZoneActor가 위치 계산 후 PlayerDetected 전송)
        _zoneActor.Tell(new ScanForPlayers(MonsterId, _position, _stats.DetectRange));
    }

    // ─────────────────────────────────────────────────────────────────────
    // STATE: Detect — 플레이어 발견, 짧은 반응 시간 후 Chase로 전환
    // ─────────────────────────────────────────────────────────────────────
    private void BecomeDetect(string playerId, Position playerPos)
    {
        _log.Debug("[Monster:{0}] → Detect (target={1})", MonsterId, playerId);
        _targetPlayerId = playerId;
        _targetPosition = playerPos;
        _zoneActor.Tell(new MonsterStateChanged(MonsterId, Domain.Enums.MonsterState.Detect));
        Become(Detect);

        // 0.5초 후 Chase 진입 (ICancelable로 관리 — PostStop에서 취소)
        _detectTimer?.Cancel();
        _detectTimer = Context.System.Scheduler.ScheduleTellOnceCancelable(
            TimeSpan.FromMilliseconds(500), Self, new BeginChase(), Self);
    }

    private void Detect()
    {
        Receive<BeginChase>(_ => BecomeChase());
        Receive<Tick>(_ => { /* 감지 상태에서는 대기 */ });
        Receive<CombatDamage>(HandleDamageInAnyState);
        Receive<PlayerPositionUpdate>(msg =>
        {
            if (msg.PlayerId == _targetPlayerId)
                _targetPosition = msg.Position;
        });
    }

    // ─────────────────────────────────────────────────────────────────────
    // STATE: Chase — 타겟 플레이어를 향해 이동
    // ─────────────────────────────────────────────────────────────────────
    private void BecomeChase()
    {
        _log.Debug("[Monster:{0}] → Chase", MonsterId);
        _zoneActor.Tell(new MonsterStateChanged(MonsterId, Domain.Enums.MonsterState.Chase));
        Become(Chase);
    }

    private void Chase()
    {
        Receive<Tick>(_ => TickChase());
        Receive<CombatDamage>(HandleDamageInAnyState);
        Receive<PlayerPositionUpdate>(msg =>
        {
            if (msg.PlayerId == _targetPlayerId)
                _targetPosition = msg.Position;
        });
        Receive<PlayerLeftZone>(msg =>
        {
            if (msg.PlayerId == _targetPlayerId)
                BecomeReturn();
        });
    }

    private void TickChase()
    {
        if (_targetPlayerId is null)
        {
            BecomeReturn();
            return;
        }

        float dist = _position.DistanceTo(_targetPosition);

        // 너무 멀어지면 복귀
        if (_position.DistanceTo(_spawnPosition) > _stats.DetectRange * 3)
        {
            BecomeReturn();
            return;
        }

        // 공격 사거리 내 진입 시 Attack으로
        if (dist <= _stats.AttackRange)
        {
            BecomeAttack();
            return;
        }

        _position = _position.MoveToward(_targetPosition, _stats.MoveSpeed * 0.1f);
        _zoneActor.Tell(new MonsterMoved(MonsterId, _position));
    }

    // ─────────────────────────────────────────────────────────────────────
    // STATE: Attack — 사거리 내에서 공격 반복
    // ─────────────────────────────────────────────────────────────────────
    private void BecomeAttack()
    {
        _log.Debug("[Monster:{0}] → Attack", MonsterId);
        _zoneActor.Tell(new MonsterStateChanged(MonsterId, Domain.Enums.MonsterState.Attack));
        Become(Attack);
    }

    private void Attack()
    {
        Receive<Tick>(_ => TickAttack());
        Receive<CombatDamage>(HandleDamageInAnyState);
        Receive<PlayerPositionUpdate>(msg =>
        {
            if (msg.PlayerId == _targetPlayerId)
                _targetPosition = msg.Position;
        });
        Receive<PlayerLeftZone>(msg =>
        {
            if (msg.PlayerId == _targetPlayerId)
                BecomeReturn();
        });
    }

    private void TickAttack()
    {
        if (_targetPlayerId is null)
        {
            BecomeReturn();
            return;
        }

        float dist = _position.DistanceTo(_targetPosition);

        // 사거리 벗어나면 다시 추적
        if (dist > _stats.AttackRange * 1.2f)
        {
            BecomeChase();
            return;
        }

        // 공격 쿨다운 체크
        if ((DateTime.UtcNow - _lastAttackTime).TotalMilliseconds < AttackCooldownMs)
            return;

        _lastAttackTime = DateTime.UtcNow;
        bool crit = _rng.NextSingle() < 0.10f;
        int damage = _stats.Attack + (crit ? _stats.Attack / 2 : 0);

        _zoneActor.Tell(new CombatDamage(MonsterId, _targetPlayerId, damage, crit));
        _log.Debug("[Monster:{0}] Attacked {1} for {2}{3}",
            MonsterId, _targetPlayerId, damage, crit ? "(CRIT)" : "");
    }

    // ─────────────────────────────────────────────────────────────────────
    // STATE: Return — 스폰 위치로 귀환, HP 회복
    // ─────────────────────────────────────────────────────────────────────
    private void BecomeReturn()
    {
        _log.Debug("[Monster:{0}] → Return", MonsterId);
        _targetPlayerId = null;
        _zoneActor.Tell(new MonsterStateChanged(MonsterId, Domain.Enums.MonsterState.Return));
        Become(Return);
    }

    private void Return()
    {
        Receive<Tick>(_ => TickReturn());
        Receive<CombatDamage>(HandleDamageInAnyState);
        Receive<PlayerDetected>(msg => BecomeDetect(msg.PlayerId, msg.Position));
    }

    private void TickReturn()
    {
        if (_position.DistanceTo(_spawnPosition) < 0.5f)
        {
            // 귀환 완료 — 체력 전회복 후 순찰 재개
            _currentHp = _stats.MaxHp;
            BecomePatrol();
            return;
        }

        _position = _position.MoveToward(_spawnPosition, _stats.MoveSpeed * 0.15f);
        _zoneActor.Tell(new MonsterMoved(MonsterId, _position));

        // 귀환 중 HP 회복 (초당 10%)
        _currentHp = Math.Min(_currentHp + (_stats.MaxHp / 100), _stats.MaxHp);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 공통: 피격 처리 (모든 상태에서 동일)
    // ─────────────────────────────────────────────────────────────────────
    private void HandleDamageInAnyState(CombatDamage evt)
    {
        if (evt.TargetId != MonsterId) return;

        _currentHp -= evt.Damage;
        _log.Debug("[Monster:{0}] HP={1}/{2} (dmg={3})", MonsterId, _currentHp, _stats.MaxHp, evt.Damage);

        if (_currentHp <= 0)
        {
            _zoneActor.Tell(new MonsterDied(MonsterId, evt.AttackerId.StartsWith("player_") ? evt.AttackerId : null));
            Context.Stop(Self);
        }
        else if (_targetPlayerId is null)
        {
            // 피격 시 공격자를 타겟으로 설정 (귀환/순찰 중 피격)
            BecomeChase();
        }
    }

    protected override void PostStop()
    {
        _detectTimer?.Cancel();
    }

    // ─── 몬스터 전용 내부 메시지 ──────────────────────────────────────
    public sealed record ScanForPlayers(string MonsterId, Position Position, float Range);
    public sealed record PlayerDetected(string PlayerId, Position Position);
    public sealed record PlayerPositionUpdate(string PlayerId, Position Position);
    private sealed record BeginChase;
}
