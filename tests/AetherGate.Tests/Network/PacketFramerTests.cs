using System.Net;
using System.Net.Sockets;
using AetherGate.Application.Network;
using AetherGate.Domain.Network;
using AetherGate.Domain.Network.Packets;
using Xunit;

namespace AetherGate.Tests.Network;

/// <summary>
/// PacketFramer — TCP 프레임 읽기/쓰기 테스트.
/// 실제 TcpListener/TcpClient 쌍을 사용해 loopback으로 검증.
/// (소켓 쌍이므로 외부 서버 불필요)
/// </summary>
public sealed class PacketFramerTests : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly TcpClient   _client;
    private readonly TcpClient   _server;

    public PacketFramerTests()
    {
        // loopback 소켓 쌍 초기화
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _client  = new TcpClient();
        _client.Connect(IPAddress.Loopback, port);
        _server  = _listener.AcceptTcpClient();
    }

    // ─── 단일 패킷 왕복 ────────────────────────────────────────────────

    [Fact]
    public async Task WriteAndRead_SinglePacket_RestoresHeaderAndPayload()
    {
        var payload    = PacketSerializer.Serialize(new MovePacket { X = 10f, Y = 20f });
        var clientStream = _client.GetStream();
        var serverStream = _server.GetStream();

        await PacketFramer.WritePacketAsync(
            clientStream, PacketId.Move, payload, sequence: 1, CancellationToken.None);

        var result = await PacketFramer.ReadPacketAsync(serverStream, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(PacketId.Move,       result!.Value.Header.PacketId);
        Assert.Equal((ushort)1,           result!.Value.Header.Sequence);
        Assert.Equal((uint)payload.Length, result!.Value.Header.Length);

        var packet = PacketSerializer.Deserialize<MovePacket>(result.Value.Payload);
        Assert.Equal(10f, packet.X, precision: 3);
        Assert.Equal(20f, packet.Y, precision: 3);
    }

    [Fact]
    public async Task WriteAndRead_MultiplePackets_InOrder()
    {
        var clientStream = _client.GetStream();
        var serverStream = _server.GetStream();

        // 3개의 패킷 연속 전송
        var packets = new[]
        {
            (PacketId.Move,  PacketSerializer.Serialize(new MovePacket { X = 1f, Y = 1f }), (ushort)0),
            (PacketId.Move,  PacketSerializer.Serialize(new MovePacket { X = 2f, Y = 2f }), (ushort)1),
            (PacketId.ZoneChat, PacketSerializer.Serialize(new ZoneChatPacket { Message = "hi" }), (ushort)2),
        };

        foreach (var (id, data, seq) in packets)
            await PacketFramer.WritePacketAsync(clientStream, id, data, seq, CancellationToken.None);

        // 순서대로 읽히는지 확인
        for (int i = 0; i < packets.Length; i++)
        {
            var result = await PacketFramer.ReadPacketAsync(serverStream, CancellationToken.None);
            Assert.NotNull(result);
            Assert.Equal(packets[i].Item1, result!.Value.Header.PacketId);
            Assert.Equal(packets[i].Item3, result!.Value.Header.Sequence);
        }
    }

    [Fact]
    public async Task WriteAndRead_EmptyPayload_Works()
    {
        var clientStream = _client.GetStream();
        var serverStream = _server.GetStream();

        await PacketFramer.WritePacketAsync(
            clientStream, PacketId.PlayerLeft, Array.Empty<byte>(), 0, CancellationToken.None);

        var result = await PacketFramer.ReadPacketAsync(serverStream, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(PacketId.PlayerLeft, result!.Value.Header.PacketId);
        Assert.Equal(0u, result!.Value.Header.Length);
        Assert.Empty(result!.Value.Payload);
    }

    [Fact]
    public async Task Read_AfterConnectionClose_ReturnsNull()
    {
        var serverStream = _server.GetStream();

        // 클라이언트 즉시 종료
        _client.Close();

        var result = await PacketFramer.ReadPacketAsync(serverStream, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task WriteAndRead_LargePayload_IntegrityMaintained()
    {
        var clientStream = _client.GetStream();
        var serverStream = _server.GetStream();

        // 10KB 가량의 메시지
        var bigMessage  = new string('A', 10_000);
        var chatPacket  = new ZoneChatPacket { Message = bigMessage };
        var payload     = PacketSerializer.Serialize(chatPacket);

        await PacketFramer.WritePacketAsync(
            clientStream, PacketId.ZoneChat, payload, 0, CancellationToken.None);

        var result = await PacketFramer.ReadPacketAsync(serverStream, CancellationToken.None);

        Assert.NotNull(result);
        var decoded = PacketSerializer.Deserialize<ZoneChatPacket>(result!.Value.Payload);
        Assert.Equal(bigMessage, decoded.Message);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        _server.Dispose();
        _listener.Stop();
        await Task.CompletedTask;
    }
}
