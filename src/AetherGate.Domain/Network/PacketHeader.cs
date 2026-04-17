namespace AetherGate.Domain.Network;

/// <summary>
/// 모든 패킷의 고정 헤더 (8 bytes).
///
/// 레이아웃:
/// ┌──────────────────────────────────────┐
/// │ Length   : 4 bytes (uint)  페이로드 길이 │
/// │ PacketId : 2 bytes (ushort) 패킷 타입  │
/// │ Sequence : 2 bytes (ushort) 순서 번호  │
/// └──────────────────────────────────────┘
/// </summary>
public readonly struct PacketHeader
{
    public const int Size = 8;

    public uint Length { get; init; }       // 페이로드 바이트 수 (헤더 제외)
    public PacketId PacketId { get; init; }
    public ushort Sequence { get; init; }

    public static PacketHeader Read(ReadOnlySpan<byte> buf)
    {
        return new PacketHeader
        {
            Length   = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buf[0..]),
            PacketId = (PacketId)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buf[4..]),
            Sequence = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buf[6..]),
        };
    }

    public void Write(Span<byte> buf)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf[0..], Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(buf[4..], (ushort)PacketId);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(buf[6..], Sequence);
    }
}
