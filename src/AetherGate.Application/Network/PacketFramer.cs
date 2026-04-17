using System.Net.Sockets;
using AetherGate.Domain.Network;

namespace AetherGate.Application.Network;

/// <summary>
/// TCP 스트림에서 패킷 프레임을 읽고 쓰는 유틸리티.
///
/// TCP는 스트림 — 하나의 Read 호출이 여러 패킷을 합쳐서 주거나
/// 하나의 패킷을 쪼개서 줄 수 있음. 이를 헤더의 Length 필드로 처리.
///
/// 읽기 흐름:
///   1. 헤더 8바이트 정확히 읽기
///   2. header.Length 만큼 페이로드 읽기
///   3. (PacketHeader, payload) 반환
/// </summary>
public static class PacketFramer
{
    /// <summary>
    /// NetworkStream에서 완전한 패킷 1개를 비동기로 읽어 반환.
    /// 연결이 끊기면 null 반환.
    /// </summary>
    public static async Task<(PacketHeader Header, byte[] Payload)?> ReadPacketAsync(
        NetworkStream stream, CancellationToken ct)
    {
        // 1. 헤더 읽기 (정확히 8바이트)
        var headerBuf = new byte[PacketHeader.Size];
        if (!await ReadExactAsync(stream, headerBuf, ct))
            return null;

        var header = PacketHeader.Read(headerBuf);

        // 2. 페이로드 읽기
        if (header.Length == 0)
            return (header, Array.Empty<byte>());

        var payload = new byte[header.Length];
        if (!await ReadExactAsync(stream, payload, ct))
            return null;

        return (header, payload);
    }

    /// <summary>
    /// 패킷을 프레임(헤더 + 페이로드)으로 조립해서 NetworkStream에 전송.
    /// </summary>
    public static async Task WritePacketAsync(
        NetworkStream stream, PacketId packetId, byte[] payload,
        ushort sequence, CancellationToken ct)
    {
        var header = new PacketHeader
        {
            Length   = (uint)payload.Length,
            PacketId = packetId,
            Sequence = sequence,
        };

        var frame = new byte[PacketHeader.Size + payload.Length];
        header.Write(frame.AsSpan(0, PacketHeader.Size));
        payload.CopyTo(frame, PacketHeader.Size);

        await stream.WriteAsync(frame, ct);
    }

    /// <summary>
    /// 스트림에서 정확히 count 바이트를 읽을 때까지 반복.
    /// 연결 끊김(0바이트 수신)이면 false 반환.
    /// </summary>
    private static async Task<bool> ReadExactAsync(
        NetworkStream stream, byte[] buf, CancellationToken ct)
    {
        int offset = 0;
        int remaining = buf.Length;

        while (remaining > 0)
        {
            int read = await stream.ReadAsync(buf.AsMemory(offset, remaining), ct);
            if (read == 0) return false;  // 연결 끊김
            offset    += read;
            remaining -= read;
        }

        return true;
    }
}
