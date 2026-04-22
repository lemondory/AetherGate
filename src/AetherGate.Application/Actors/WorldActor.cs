using Akka.Actor;
using Akka.Event;
using AetherGate.Application.Actors.Zone;
using AetherGate.Domain.Messages.Commands;
using AetherGate.Domain.Messages.Events;

namespace AetherGate.Application.Actors;

/// <summary>
/// 월드 전체를 관리. 상시 Field Zone과 동적 Dungeon Zone 인스턴스를 감독.
/// </summary>
public sealed class WorldActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly Dictionary<string, IActorRef> _fieldZones      = new();
    private readonly Dictionary<string, IActorRef> _dungeonInstances = new();

    // 플레이어 → 현재 소속 ZoneActor 매핑 (Admin Kick에서 사용)
    private readonly Dictionary<string, IActorRef> _playerZones = new();

    public WorldActor()
    {
        Receive<EnterZoneRequest>(HandleEnterZone);
        Receive<LeaveZoneRequest>(HandleLeaveZone);
        Receive<EnterDungeonRequest>(HandleEnterDungeon);
        Receive<DungeonClearRequest>(HandleDungeonClear);
        Receive<DungeonFailRequest>(HandleDungeonFail);
        Receive<KickPlayerRequest>(HandleKickPlayer);
        Receive<AdminBroadcastRequest>(HandleAdminBroadcast);
        Receive<Terminated>(HandleZoneTerminated);
    }

    protected override void PreStart()
    {
        CreateFieldZone("field_01", "FieldMap_Grassland");
        _log.Info("[World] World initialized. Field zones: {0}", _fieldZones.Count);
    }

    private void CreateFieldZone(string zoneId, string mapId)
    {
        var cfg  = new ZoneConfig(zoneId, mapId, false);
        var zone = Context.ActorOf(
            Props.Create(() => new ZoneActor(cfg)),
            $"zone-{zoneId}");
        Context.Watch(zone);
        _fieldZones[zoneId] = zone;
        _log.Info("[World] Field zone created: {0}", zoneId);
    }

    private void HandleEnterZone(EnterZoneRequest req)
    {
        var zone = FindZone(req.ZoneId);
        if (zone is null)
        {
            _log.Warning("[World] Zone not found: {0}", req.ZoneId);
            return;
        }

        zone.Tell(req);
        zone.Tell(new SessionEnrollment(req.PlayerId, Sender));
        _playerZones[req.PlayerId] = zone;
    }

    private void HandleLeaveZone(LeaveZoneRequest req)
    {
        FindZone(req.ZoneId)?.Tell(req);
        _playerZones.Remove(req.PlayerId);
    }

    private void HandleEnterDungeon(EnterDungeonRequest req)
    {
        var sessionActor = Sender;
        string instanceId = $"dungeon_{req.DungeonTemplateId}_{Guid.NewGuid().ToString("N")[..8]}";

        var dungeonCfg = new ZoneConfig(instanceId, req.DungeonTemplateId, true);
        var dungeon = Context.ActorOf(
            Props.Create(() => new ZoneActor(dungeonCfg)),
            $"zone-{instanceId}");
        Context.Watch(dungeon);
        _dungeonInstances[instanceId] = dungeon;

        _log.Info("[World] Dungeon instance created: {0} for player {1}", instanceId, req.PlayerId);

        sessionActor.Tell(new DungeonInstanceCreated(instanceId, req.PlayerId));

        dungeon.Tell(new EnterZoneRequest(
            req.PlayerId, instanceId, new Domain.ValueObjects.Position(10, 10)));
        dungeon.Tell(new SessionEnrollment(req.PlayerId, sessionActor));
        _playerZones[req.PlayerId] = dungeon;
    }

    private void HandleDungeonClear(DungeonClearRequest req)
    {
        if (_dungeonInstances.Remove(req.ZoneInstanceId, out var zone))
        {
            _log.Info("[World] Dungeon cleared: {0}", req.ZoneInstanceId);
            zone.Tell(new Terminate());
        }
    }

    private void HandleDungeonFail(DungeonFailRequest req)
    {
        if (_dungeonInstances.Remove(req.ZoneInstanceId, out var zone))
        {
            _log.Info("[World] Dungeon failed: {0}", req.ZoneInstanceId);
            zone.Tell(new Terminate());
        }
    }

    // ─── Admin 명령 처리 ──────────────────────────────────────────────

    private void HandleKickPlayer(KickPlayerRequest req)
    {
        if (_playerZones.TryGetValue(req.PlayerId, out var zone))
        {
            // 플레이어가 속한 Zone에 LeaveZoneRequest 전송 → SessionActor 연결 해제
            zone.Tell(new LeaveZoneRequest(req.PlayerId, ""));
            _playerZones.Remove(req.PlayerId);
            _log.Info("[World] Admin kicked player: {0}", req.PlayerId);
        }
        else
        {
            _log.Warning("[World] Kick failed — player not found: {0}", req.PlayerId);
        }
    }

    private void HandleAdminBroadcast(AdminBroadcastRequest req)
    {
        foreach (var zone in _fieldZones.Values)
            zone.Tell(req);
        foreach (var dungeon in _dungeonInstances.Values)
            dungeon.Tell(req);
        _log.Info("[World] Admin broadcast sent to {0} zones",
            _fieldZones.Count + _dungeonInstances.Count);
    }

    private void HandleZoneTerminated(Terminated msg)
    {
        var path   = msg.ActorRef.Path.Name;
        var zoneId = path.StartsWith("zone-") ? path["zone-".Length..] : path;
        _log.Warning("[World] Zone actor terminated: {0}", zoneId);
        _dungeonInstances.Remove(zoneId);

        // 해당 Zone 소속 플레이어 매핑 정리
        var orphaned = _playerZones
            .Where(kv => kv.Value.Equals(msg.ActorRef))
            .Select(kv => kv.Key)
            .ToList();
        foreach (var pid in orphaned)
            _playerZones.Remove(pid);
    }

    private IActorRef? FindZone(string zoneId)
    {
        if (_fieldZones.TryGetValue(zoneId, out var zone))       return zone;
        if (_dungeonInstances.TryGetValue(zoneId, out var inst)) return inst;
        return null;
    }

    // Application 레이어 전용 메시지 — Domain 커맨드와 분리
    public sealed record SessionEnrollment(string PlayerId, IActorRef Session);
    public sealed record Terminate;
}
