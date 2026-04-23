using AetherGate.Application.Network;
using AetherGate.Domain.Network.Packets;
using Xunit;

namespace AetherGate.Tests.Network;

/// <summary>
/// PacketSerializer — MessagePack 직렬화/역직렬화 단위 테스트.
/// 외부 의존 없음, 순수 메모리 연산.
/// </summary>
public sealed class PacketSerializerTests
{
    // ─── 왕복 직렬화 (serialize → deserialize 후 값 동일) ─────────────

    [Fact]
    public void LoginPacket_RoundTrip_PreservesAllFields()
    {
        var original = new LoginPacket
        {
            Username     = "player1",
            PasswordHash = "abc123hash",
        };

        var bytes  = PacketSerializer.Serialize(original);
        var result = PacketSerializer.Deserialize<LoginPacket>(bytes);

        Assert.Equal(original.Username,     result.Username);
        Assert.Equal(original.PasswordHash, result.PasswordHash);
    }

    [Fact]
    public void MovePacket_RoundTrip_PreservesCoordinates()
    {
        var original = new MovePacket { X = 123.45f, Y = -67.89f };

        var bytes  = PacketSerializer.Serialize(original);
        var result = PacketSerializer.Deserialize<MovePacket>(bytes);

        Assert.Equal(original.X, result.X, precision: 3);
        Assert.Equal(original.Y, result.Y, precision: 3);
    }

    [Fact]
    public void UseSkillPacket_WithOptionalFields_RoundTrip()
    {
        var original = new UseSkillPacket
        {
            SkillId  = 2,
            TargetId = null,
            TargetX  = 50.0f,
            TargetY  = 75.5f,
        };

        var bytes  = PacketSerializer.Serialize(original);
        var result = PacketSerializer.Deserialize<UseSkillPacket>(bytes);

        Assert.Equal(original.SkillId,  result.SkillId);
        Assert.Null(result.TargetId);
        Assert.Equal(original.TargetX,  result.TargetX);
        Assert.Equal(original.TargetY,  result.TargetY);
    }

    [Fact]
    public void LoginResultPacket_Success_RoundTrip()
    {
        var original = new LoginResultPacket
        {
            Success  = true,
            PlayerId = "player_player1",
            Token    = "tok_abc123",
        };

        var bytes  = PacketSerializer.Serialize(original);
        var result = PacketSerializer.Deserialize<LoginResultPacket>(bytes);

        Assert.True(result.Success);
        Assert.Equal(original.PlayerId, result.PlayerId);
        Assert.Equal(original.Token,    result.Token);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void LoginResultPacket_Failure_RoundTrip()
    {
        var original = new LoginResultPacket
        {
            Success      = false,
            ErrorMessage = "Invalid credentials",
        };

        var bytes  = PacketSerializer.Serialize(original);
        var result = PacketSerializer.Deserialize<LoginResultPacket>(bytes);

        Assert.False(result.Success);
        Assert.Equal(original.ErrorMessage, result.ErrorMessage);
        Assert.Null(result.PlayerId);
    }

    [Fact]
    public void CombatDamagePacket_RoundTrip()
    {
        var original = new CombatDamagePacket
        {
            AttackerId = "mob_field_01_3",
            TargetId   = "player_player1",
            Damage     = 42,
            IsCritical = true,
        };

        var bytes  = PacketSerializer.Serialize(original);
        var result = PacketSerializer.Deserialize<CombatDamagePacket>(bytes);

        Assert.Equal(original.AttackerId, result.AttackerId);
        Assert.Equal(original.TargetId,   result.TargetId);
        Assert.Equal(original.Damage,     result.Damage);
        Assert.True(result.IsCritical);
    }

    // ─── 직렬화 바이트 검증 ────────────────────────────────────────────

    [Fact]
    public void Serialize_ProducesNonEmptyBytes()
    {
        var packet = new MovePacket { X = 1f, Y = 2f };
        var bytes  = PacketSerializer.Serialize(packet);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void Serialize_DifferentValues_ProduceDifferentBytes()
    {
        var a = PacketSerializer.Serialize(new MovePacket { X = 1f, Y = 2f });
        var b = PacketSerializer.Serialize(new MovePacket { X = 3f, Y = 4f });
        Assert.False(a.SequenceEqual(b));
    }
}
