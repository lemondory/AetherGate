using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using AetherGate.Domain.Messages.Commands;
using StackExchange.Redis;

namespace AetherGate.Application.Actors;

/// <summary>
/// Redis "admin:commands" 채널을 구독하고 수신 메시지를 GameServerActor로 전달.
///
/// Redis Subscribe는 콜백 기반 → Channel&lt;string&gt;으로 브리지 후 PipeTo 패턴 적용.
/// 연결 끊김 시 SupervisorStrategy(Restart)로 자동 재연결.
/// </summary>
public sealed class AdminBridgeActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly string _redisConnection;
    private readonly IActorRef _gameServer;

    private ConnectionMultiplexer? _redis;
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();
    private readonly CancellationTokenSource _cts = new();

    private static readonly string ChannelName = "admin:commands";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public AdminBridgeActor(string redisConnection, IActorRef gameServer)
    {
        _redisConnection = redisConnection;
        _gameServer = gameServer;

        Receive<AdminMessageReceived>(HandleAdminMessage);
        Receive<SubscribeFailed>(msg =>
            throw new InvalidOperationException($"Redis subscribe failed: {msg.Reason}"));
    }

    protected override void PreStart()
    {
        // 동기 연결 — 초기화 시점에만 발생, 짧은 블로킹 허용
        _redis = ConnectionMultiplexer.Connect(_redisConnection);
        var subscriber = _redis.GetSubscriber();

        // Redis 콜백 → Channel 브리지
        subscriber.Subscribe(RedisChannel.Literal(ChannelName),
            (_, message) =>
            {
                if (message.HasValue)
                    _channel.Writer.TryWrite(message!);
            });

        _log.Info("[AdminBridge] Subscribed to Redis channel: {0}", ChannelName);
        StartReadLoop();
    }

    private void StartReadLoop()
    {
        ReadNextAsync(_cts.Token)
            .PipeTo(Self,
                success: json => new AdminMessageReceived(json),
                failure: ex  => new SubscribeFailed(ex.Message));
    }

    private async Task<string> ReadNextAsync(CancellationToken ct)
        => await _channel.Reader.ReadAsync(ct);

    private void HandleAdminMessage(AdminMessageReceived msg)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<AdminCommandPayload>(msg.Json, JsonOptions);
            if (payload is not null)
            {
                var cmd = new AdminCommand(payload.Type, payload.PlayerId, payload.Message);
                _gameServer.Tell(cmd);
                _log.Debug("[AdminBridge] Command received: {0} (player={1})",
                    payload.Type, payload.PlayerId ?? "-");
            }
        }
        catch (JsonException ex)
        {
            _log.Warning("[AdminBridge] Invalid JSON: {0} — {1}", msg.Json, ex.Message);
        }

        StartReadLoop(); // 다음 메시지 대기
    }

    protected override void PostStop()
    {
        _cts.Cancel();
        _redis?.Dispose();
        _log.Info("[AdminBridge] Stopped.");
    }

    // ─── 내부 메시지 ──────────────────────────────────────────────────
    private sealed record AdminMessageReceived(string Json);
    private sealed record SubscribeFailed(string Reason);

    // Redis 메시지 JSON 구조
    private sealed record AdminCommandPayload(
        AdminCommandType Type,
        string? PlayerId,
        string? Message);
}
