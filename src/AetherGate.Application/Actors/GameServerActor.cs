using Akka.Actor;
using Akka.Event;
using AetherGate.Domain.Messages.Commands;

namespace AetherGate.Application.Actors;

/// <summary>
/// 최상위 Actor — WorldActor, GatewayActor, AuthActor, AdminBridgeActor를 자식으로 보유.
/// One-For-One 전략: 자식 하나가 죽어도 다른 자식은 무영향.
/// </summary>
public sealed class GameServerActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly ITokenService _tokenService;
    private readonly string _redisConnection;

    private IActorRef _worldActor   = ActorRefs.Nobody;
    private IActorRef _gatewayActor = ActorRefs.Nobody;
    private IActorRef _authActor    = ActorRefs.Nobody;
    private IActorRef _adminBridge  = ActorRefs.Nobody;

    public GameServerActor(ITokenService tokenService, string redisConnection)
    {
        _tokenService    = tokenService;
        _redisConnection = redisConnection;

        Receive<StartServer>(_ => HandleStart());
        Receive<StopServer>(_ => HandleStop());

        // AdminBridgeActor가 파싱한 운영 명령 수신
        Receive<AdminCommand>(HandleAdminCommand);
    }

    protected override void PreStart()
    {
        _log.Info("[GameServer] Starting up...");

        var strategy = new OneForOneStrategy(
            maxNrOfRetries: 10,
            withinTimeRange: TimeSpan.FromMinutes(1),
            localOnlyDecider: ex =>
            {
                _log.Warning("[GameServer] Child failure: {0}", ex.Message);
                return Directive.Restart;
            });

        _authActor = Context.ActorOf(
            Props.Create(() => new AuthActor(_tokenService)).WithSupervisorStrategy(strategy),
            "auth");

        _worldActor = Context.ActorOf(
            Props.Create(() => new WorldActor()).WithSupervisorStrategy(strategy),
            "world");

        _gatewayActor = Context.ActorOf(
            Props.Create(() => new GatewayActor(_authActor, _worldActor)).WithSupervisorStrategy(strategy),
            "gateway");

        _adminBridge = Context.ActorOf(
            Props.Create(() => new AdminBridgeActor(_redisConnection, Self)).WithSupervisorStrategy(strategy),
            "admin-bridge");

        _log.Info("[GameServer] All child actors started.");
    }

    private void HandleStart()
    {
        _log.Info("[GameServer] Server started.");
        _gatewayActor.Tell(new StartListening(Port: 9000));
    }

    private void HandleStop()
    {
        _log.Info("[GameServer] Shutting down...");
        Context.Stop(Self);
    }

    private void HandleAdminCommand(AdminCommand cmd)
    {
        switch (cmd.Type)
        {
            case AdminCommandType.Kick when cmd.PlayerId is not null:
                _worldActor.Tell(new KickPlayerRequest(cmd.PlayerId));
                _log.Info("[GameServer] Admin kick: {0}", cmd.PlayerId);
                break;

            case AdminCommandType.Broadcast when cmd.Message is not null:
                _worldActor.Tell(new AdminBroadcastRequest(cmd.Message));
                _log.Info("[GameServer] Admin broadcast: {0}", cmd.Message);
                break;

            default:
                _log.Warning("[GameServer] Unknown admin command: {0}", cmd.Type);
                break;
        }
    }

    // ─── 내부 전용 메시지 ──────────────────────────────────────────────
    public sealed record StartServer;
    public sealed record StopServer;
    public sealed record StartListening(int Port);
}
