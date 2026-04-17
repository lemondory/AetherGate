using Akka.Actor;
using Akka.Event;
using AetherGate.Domain.Messages.Commands;

namespace AetherGate.Application.Actors;

/// <summary>
/// 최상위 Actor — WorldActor, GatewayActor, AuthActor를 자식으로 보유.
/// One-For-One 전략: 자식 하나가 죽어도 다른 자식은 무영향.
/// </summary>
public sealed class GameServerActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private IActorRef _worldActor = ActorRefs.Nobody;
    private IActorRef _gatewayActor = ActorRefs.Nobody;
    private IActorRef _authActor = ActorRefs.Nobody;

    public GameServerActor()
    {
        Receive<StartServer>(_ => HandleStart());
        Receive<StopServer>(_ => HandleStop());
    }

    protected override void PreStart()
    {
        _log.Info("[GameServer] Starting up...");

        // Supervision: 자식 Actor 장애 시 재시작, 최대 10회/1분
        var strategy = new OneForOneStrategy(
            maxNrOfRetries: 10,
            withinTimeRange: TimeSpan.FromMinutes(1),
            localOnlyDecider: ex =>
            {
                _log.Warning("[GameServer] Child failure: {0}", ex.Message);
                return Directive.Restart;
            });

        _authActor = Context.ActorOf(
            Props.Create(() => new AuthActor()).WithSupervisorStrategy(strategy),
            "auth");

        _worldActor = Context.ActorOf(
            Props.Create(() => new WorldActor()).WithSupervisorStrategy(strategy),
            "world");

        _gatewayActor = Context.ActorOf(
            Props.Create(() => new GatewayActor(_authActor, _worldActor)).WithSupervisorStrategy(strategy),
            "gateway");

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

    // ─── 내부 전용 메시지 ──────────────────────────────────────────────
    public sealed record StartServer;
    public sealed record StopServer;
    public sealed record StartListening(int Port);
}
