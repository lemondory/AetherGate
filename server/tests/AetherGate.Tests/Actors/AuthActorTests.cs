using Akka.Actor;
using Akka.TestKit.Xunit2;
using AetherGate.Application.Actors;
using AetherGate.Domain.Messages.Commands;
using AetherGate.Domain.Messages.Events;
using Xunit;

namespace AetherGate.Tests.Actors;

/// <summary>
/// AuthActor 단위 테스트.
/// 로그인 성공/실패, 토큰 발급, 토큰 검증 흐름 검증.
/// </summary>
public sealed class AuthActorTests : TestKit
{
    // AuthActor에 등록된 데모 계정 (admin / sha256("admin"))
    private const string ValidUsername    = "admin";
    private const string ValidHash        = "8c6976e5b5410415bde908bd4dee15dfb167a9c873fc4bb8a81f6f2ab448a918";
    private const string InvalidHash      = "0000000000000000000000000000000000000000000000000000000000000000";
    private const string UnknownUsername  = "nobody";

    // ─── 로그인 성공 ──────────────────────────────────────────────────

    [Fact]
    public void Login_WithValidCredentials_ReturnsLoginSucceeded()
    {
        var auth = CreateAuth();

        auth.Tell(new LoginRequest(ValidUsername, ValidHash));

        ExpectMsg<LoginSucceeded>(msg =>
        {
            Assert.Equal("player_admin", msg.PlayerId);
            Assert.False(string.IsNullOrWhiteSpace(msg.Token));
        }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Login_Success_TokenIsNonEmpty()
    {
        var auth = CreateAuth();

        auth.Tell(new LoginRequest(ValidUsername, ValidHash));

        var result = ExpectMsg<LoginSucceeded>(TimeSpan.FromSeconds(1));
        Assert.StartsWith("tok_", result.Token); // DefaultTokenService 형식 검증
    }

    // ─── 로그인 실패 ──────────────────────────────────────────────────

    [Fact]
    public void Login_WithWrongPassword_ReturnsLoginFailed()
    {
        var auth = CreateAuth();

        auth.Tell(new LoginRequest(ValidUsername, InvalidHash));

        ExpectMsg<LoginFailed>(msg =>
        {
            Assert.False(string.IsNullOrWhiteSpace(msg.Reason));
        }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Login_WithUnknownUsername_ReturnsLoginFailed()
    {
        var auth = CreateAuth();

        auth.Tell(new LoginRequest(UnknownUsername, ValidHash));

        ExpectMsg<LoginFailed>(TimeSpan.FromSeconds(1));
    }

    // ─── 토큰 검증 ───────────────────────────────────────────────────

    [Fact]
    public void ValidateToken_AfterLogin_ReturnsIsValid()
    {
        var auth = CreateAuth();

        // 로그인해서 토큰 발급
        auth.Tell(new LoginRequest(ValidUsername, ValidHash));
        var login = ExpectMsg<LoginSucceeded>(TimeSpan.FromSeconds(1));

        // 발급된 토큰 검증
        auth.Tell(new ValidateTokenRequest(login.Token));

        ExpectMsg<TokenValidated>(msg =>
        {
            Assert.True(msg.IsValid);
            Assert.Equal("player_admin", msg.PlayerId);
        }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ValidateToken_WithBogusToken_ReturnsNotValid()
    {
        var auth = CreateAuth();

        auth.Tell(new ValidateTokenRequest("tok_bogus_does_not_exist"));

        ExpectMsg<TokenValidated>(msg =>
        {
            Assert.False(msg.IsValid);
        }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ValidateToken_WithEmptyToken_ReturnsNotValid()
    {
        var auth = CreateAuth();

        auth.Tell(new ValidateTokenRequest(string.Empty));

        ExpectMsg<TokenValidated>(msg =>
        {
            Assert.False(msg.IsValid);
        }, TimeSpan.FromSeconds(1));
    }

    // ─── 다중 로그인 ──────────────────────────────────────────────────

    [Fact]
    public void MultipleLogins_EachProduceDifferentTokens()
    {
        var auth = CreateAuth();

        auth.Tell(new LoginRequest(ValidUsername, ValidHash));
        var first = ExpectMsg<LoginSucceeded>(TimeSpan.FromSeconds(1));

        auth.Tell(new LoginRequest(ValidUsername, ValidHash));
        var second = ExpectMsg<LoginSucceeded>(TimeSpan.FromSeconds(1));

        // 매번 새 토큰 발급
        Assert.NotEqual(first.Token, second.Token);
    }

    [Fact]
    public void BothTokens_AfterMultipleLogins_AreValid()
    {
        var auth = CreateAuth();

        auth.Tell(new LoginRequest(ValidUsername, ValidHash));
        var first = ExpectMsg<LoginSucceeded>(TimeSpan.FromSeconds(1));

        auth.Tell(new LoginRequest(ValidUsername, ValidHash));
        var second = ExpectMsg<LoginSucceeded>(TimeSpan.FromSeconds(1));

        // 두 토큰 모두 유효
        auth.Tell(new ValidateTokenRequest(first.Token));
        ExpectMsg<TokenValidated>(msg => Assert.True(msg.IsValid), TimeSpan.FromSeconds(1));

        auth.Tell(new ValidateTokenRequest(second.Token));
        ExpectMsg<TokenValidated>(msg => Assert.True(msg.IsValid), TimeSpan.FromSeconds(1));
    }

    // ─── 헬퍼 ────────────────────────────────────────────────────────

    private IActorRef CreateAuth() =>
        Sys.ActorOf(Props.Create<AuthActor>());
}
