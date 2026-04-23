using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Event;
using AetherGate.Domain.Messages.Commands;

namespace AetherGate.Application.Actors;

/// <summary>
/// 클라이언트 TCP 연결을 수신하고 SessionActor를 동적으로 생성/소멸.
///
/// 핵심 패턴:
/// - AcceptLoop는 별도 Task에서 실행 → PipeTo(Self)로 결과를 메시지화
/// - Actor 내부에서 직접 await 금지 (스레드 안전성 보장)
/// </summary>
public sealed class GatewayActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _authActor;
    private readonly IActorRef _worldActor;
    private readonly Dictionary<string, IActorRef> _sessions = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public GatewayActor(IActorRef authActor, IActorRef worldActor)
    {
        _authActor = authActor;
        _worldActor = worldActor;

        Receive<GameServerActor.StartListening>(HandleStartListening);
        Receive<RawClientAccepted>(HandleRawClientAccepted);
        Receive<ClientDisconnected>(HandleClientDisconnected);
        Receive<AcceptFailed>(msg =>
            _log.Error("[Gateway] Accept loop failed: {0}", msg.Reason));
        Receive<Terminated>(HandleSessionTerminated);
    }

    private void HandleStartListening(GameServerActor.StartListening msg)
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, msg.Port);
        _listener.Start();

        _log.Info("[Gateway] Listening on 0.0.0.0:{0}", msg.Port);

        // AcceptLoop를 별도 Task에서 실행, 결과는 PipeTo로 Self에 전달
        StartAcceptLoop();
    }

    private void StartAcceptLoop()
    {
        // PipeTo 패턴: Task 결과를 Actor 메시지로 변환
        // 성공 → RawClientAccepted, 실패 → AcceptFailed
        AcceptNextAsync(_cts!.Token)
            .PipeTo(Self,
                success: client => new RawClientAccepted(client),
                failure: ex  => new AcceptFailed(ex.Message));
    }

    private async Task<TcpClient> AcceptNextAsync(CancellationToken ct)
    {
        return await _listener!.AcceptTcpClientAsync(ct);
    }

    private void HandleRawClientAccepted(RawClientAccepted msg)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..12];
        var remoteEndPoint = msg.Client.Client.RemoteEndPoint as IPEndPoint;

        _log.Info("[Gateway] Client connected: {0} from {1}", sessionId, remoteEndPoint);

        // SessionActor 생성 — TcpClient를 직접 전달
        var sessionActor = Context.ActorOf(
            Props.Create(() => new SessionActor(sessionId, msg.Client, _authActor, _worldActor)),
            $"session-{sessionId}");

        _sessions[sessionId] = sessionActor;
        Context.Watch(sessionActor);

        // 다음 연결을 즉시 대기 (루프 유지)
        StartAcceptLoop();
    }

    private void HandleClientDisconnected(ClientDisconnected msg)
    {
        if (_sessions.Remove(msg.SessionId, out var sessionActor))
        {
            _log.Info("[Gateway] Session removed: {0}", msg.SessionId);
            Context.Stop(sessionActor);
        }
    }

    private void HandleSessionTerminated(Terminated msg)
    {
        // Watch로 감시 중인 SessionActor가 예기치 않게 종료된 경우
        var entry = _sessions.FirstOrDefault(kv => kv.Value.Equals(msg.ActorRef));
        if (entry.Key is not null)
        {
            _sessions.Remove(entry.Key);
            _log.Warning("[Gateway] SessionActor terminated unexpectedly: {0}", entry.Key);
        }
    }

    protected override void PostStop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _listener?.Stop();
        _log.Info("[Gateway] Stopped. Total sessions cleaned: {0}", _sessions.Count);
    }

    // ─── 내부 메시지 ──────────────────────────────────────────────────
    private sealed record RawClientAccepted(TcpClient Client);
    private sealed record AcceptFailed(string Reason);
}
