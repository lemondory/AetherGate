using MessagePack;

namespace AetherGate.Application.Network;

/// <summary>
/// MessagePack 기반 패킷 직렬화/역직렬화.
/// MessagePackSerializerOptions.Standard 사용 (ContractlessStandardResolver 아님 — Key 어트리뷰트 강제).
/// </summary>
public static class PacketSerializer
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard;

    public static byte[] Serialize<T>(T value) =>
        MessagePackSerializer.Serialize(value, Options);

    public static T Deserialize<T>(byte[] data) =>
        MessagePackSerializer.Deserialize<T>(data, Options);

    public static T Deserialize<T>(ReadOnlyMemory<byte> data) =>
        MessagePackSerializer.Deserialize<T>(data, Options);
}
