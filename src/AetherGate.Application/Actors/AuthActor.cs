using Akka.Actor;
using Akka.Event;
using AetherGate.Domain.Messages.Commands;
using AetherGate.Domain.Messages.Events;

namespace AetherGate.Application.Actors;

/// <summary>
/// JWT 발급 / 검증 담당 Actor.
/// 실제 프로젝트에서는 ITokenService를 DI로 주입하고 Infrastructure 레이어를 사용.
/// 여기서는 간단한 인메모리 구현으로 데모.
/// </summary>
public sealed class AuthActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();

    // 실제 환경에서는 DB 연동 — 데모용 인메모리 계정
    private static readonly Dictionary<string, string> _accounts = new()
    {
        ["admin"] = "8c6976e5b5410415bde908bd4dee15dfb167a9c873fc4bb8a81f6f2ab448a918", // admin
        ["player1"] = "0a041b9462caa4a31bac3567e0b6e6fd9100787db2ab433d96f6d178cabfce90" // password1
    };

    // 발급된 토큰 → PlayerId 매핑 (실제로는 Redis 등)
    private readonly Dictionary<string, string> _issuedTokens = new();
    private readonly ITokenService _tokenService;

    public AuthActor() : this(new DefaultTokenService()) { }

    public AuthActor(ITokenService tokenService)
    {
        _tokenService = tokenService;
        Receive<LoginRequest>(HandleLogin);
        Receive<ValidateTokenRequest>(HandleValidate);
    }

    private void HandleLogin(LoginRequest req)
    {
        _log.Debug("[Auth] Login attempt for user: {0}", req.Username);

        if (!_accounts.TryGetValue(req.Username, out var storedHash) ||
            storedHash != req.PasswordHash)
        {
            Sender.Tell(new LoginFailed("Invalid credentials"));
            return;
        }

        string playerId = $"player_{req.Username}";
        string token = _tokenService.GenerateToken(playerId, req.Username);
        _issuedTokens[token] = playerId;

        _log.Info("[Auth] Login success: {0} → {1}", req.Username, playerId);
        Sender.Tell(new LoginSucceeded(playerId, token));
    }

    private void HandleValidate(ValidateTokenRequest req)
    {
        if (_issuedTokens.TryGetValue(req.Token, out var playerId) &&
            _tokenService.ValidateToken(req.Token, out _))
        {
            Sender.Tell(new TokenValidated(playerId, true));
        }
        else
        {
            Sender.Tell(new TokenValidated(string.Empty, false));
        }
    }
}

// ─── Token Service 인터페이스 (Infrastructure로 DI 교체 가능) ─────────
public interface ITokenService
{
    string GenerateToken(string playerId, string username);
    bool ValidateToken(string token, out string playerId);
}

internal sealed class DefaultTokenService : ITokenService
{
    private readonly Dictionary<string, (string PlayerId, DateTime Expires)> _store = new();

    public string GenerateToken(string playerId, string username)
    {
        string token = $"tok_{Guid.NewGuid():N}";
        _store[token] = (playerId, DateTime.UtcNow.AddHours(24));
        return token;
    }

    public bool ValidateToken(string token, out string playerId)
    {
        if (_store.TryGetValue(token, out var info) && info.Expires > DateTime.UtcNow)
        {
            playerId = info.PlayerId;
            return true;
        }
        playerId = string.Empty;
        return false;
    }
}
