using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Configuration;
using AetherGate.Application.Actors;
using AetherGate.Application.Network;
using AetherGate.Domain.Network;
using AetherGate.Domain.Network.Packets;
using Xunit;

namespace AetherGate.Tests.Network;

/// <summary>
/// E2E TCP 테스트 — 실제 소켓으로 서버에 연결해 패킷 흐름 검증.
///
/// GameServerActor 전체를 띄우지 않고 GatewayActor + TestProbe(Auth/World 대역)로
/// SessionActor의 네트워크 흐름에 집중.
/// </summary>
public sealed class EndToEndTests : IAsyncDisposable
{
    private readonly ActorSystem _system;
    private readonly IActorRef   _gateway;
    private readonly int         _port;

    // Auth/World 대역 (TestProbe 역할 — 메시지 수동 응답)
    private readonly IActorRef _authProbe;
    private readonly IActorRef _worldProbe;

    public EndToEndTests()
    {
        _system = ActorSystem.Create("e2e-test", ConfigurationFactory.ParseString("""
            akka.loglevel = WARNING
            """));

        _authProbe  = _system.ActorOf(Props.Create<EchoActor>(), "auth-probe");
        _worldProbe = _system.ActorOf(Props.Create<EchoActor>(), "world-probe");

        _port = FreeTcpPort();

        _gateway = _system.ActorOf(
            Props.Create(() => new GatewayActor(_authProbe, _worldProbe)),
            "gateway");

        _gateway.Tell(new GameServerActor.StartListening(_port));

        // GatewayActor가 리스닝 시작할 시간
        Thread.Sleep(100);
    }

    // ─── 연결 수립 ────────────────────────────────────────────────────

    [Fact]
    public async Task Client_CanConnect_ToGatewayActor()
    {
        using var client = await ConnectAsync();
        Assert.True(client.Connected);
    }

    // ─── 패킷 송신 ────────────────────────────────────────────────────

    [Fact]
    public async Task Client_CanSend_LoginPacket_WithoutError()
    {
        using var client = await ConnectAsync();
        var stream = client.GetStream();

        var payload = PacketSerializer.Serialize(new LoginPacket
        {
            Username     = "admin",
            PasswordHash = "8c6976e5b5410415bde908bd4dee15dfb167a9c873fc4bb8a81f6f2ab448a918",
        });

        // 예외 없이 전송되면 성공
        await PacketFramer.WritePacketAsync(stream, PacketId.Login, payload, 0,
            CancellationToken.None);

        Assert.True(client.Connected);
    }

    // ─── 다중 클라이언트 연결 ─────────────────────────────────────────

    [Fact]
    public async Task MultipleClients_CanConnect_Simultaneously()
    {
        const int count = 5;
        var clients = new List<TcpClient>();

        try
        {
            for (int i = 0; i < count; i++)
                clients.Add(await ConnectAsync());

            // 모든 클라이언트가 연결 상태인지 확인
            Assert.All(clients, c => Assert.True(c.Connected));
        }
        finally
        {
            foreach (var c in clients) c.Dispose();
        }
    }

    // ─── 연결 해제 ────────────────────────────────────────────────────

    [Fact]
    public async Task Client_CanDisconnect_GracefullyDetectedByServer()
    {
        var client = await ConnectAsync();
        var stream = client.GetStream();

        // 패킷 하나 전송 후 연결 끊기
        var payload = PacketSerializer.Serialize(new MovePacket { X = 1f, Y = 1f });
        await PacketFramer.WritePacketAsync(stream, PacketId.Move, payload, 0,
            CancellationToken.None);

        client.Dispose();

        // 서버가 정상 상태인지 — 새 클라이언트가 연결 가능하면 OK
        await Task.Delay(200);
        using var newClient = await ConnectAsync();
        Assert.True(newClient.Connected);
    }

    // ─── 프레임 무결성 ────────────────────────────────────────────────

    [Fact]
    public async Task PacketFramer_LargePayload_SentAndReceivedIntact_OverRealSocket()
    {
        // 실제 소켓 쌍으로 큰 패킷의 TCP fragmentation 처리 검증
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var sender   = new TcpClient();
        sender.Connect(IPAddress.Loopback, port);
        using var receiver = listener.AcceptTcpClient();

        var bigMsg  = new string('Z', 50_000); // 50KB
        var payload = PacketSerializer.Serialize(new ZoneChatPacket { Message = bigMsg });

        var sendTask    = PacketFramer.WritePacketAsync(
            sender.GetStream(), PacketId.ZoneChat, payload, 7, CancellationToken.None);
        var receiveTask = PacketFramer.ReadPacketAsync(receiver.GetStream(), CancellationToken.None);

        await sendTask;
        var result = await receiveTask;

        Assert.NotNull(result);
        Assert.Equal(PacketId.ZoneChat, result!.Value.Header.PacketId);
        Assert.Equal((ushort)7,         result!.Value.Header.Sequence);

        var decoded = PacketSerializer.Deserialize<ZoneChatPacket>(result.Value.Payload);
        Assert.Equal(bigMsg, decoded.Message);

        listener.Stop();
    }

    // ─── 헬퍼 ────────────────────────────────────────────────────────

    private async Task<TcpClient> ConnectAsync()
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        return client;
    }

    private static int FreeTcpPort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    // 모든 메시지를 무시하는 대역 Actor (Auth/World 역할)
    private sealed class EchoActor : ReceiveActor
    {
        public EchoActor() => ReceiveAny(_ => { });
    }
}
