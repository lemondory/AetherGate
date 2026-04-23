namespace AetherGate.Domain.Messages.Commands;

// ─── Auth Commands ────────────────────────────────────────────────────
public sealed record LoginRequest(string Username, string PasswordHash);
public sealed record ValidateTokenRequest(string Token);
