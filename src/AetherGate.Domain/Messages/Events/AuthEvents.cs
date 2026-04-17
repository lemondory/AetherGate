namespace AetherGate.Domain.Messages.Events;

public sealed record LoginSucceeded(string PlayerId, string Token);
public sealed record LoginFailed(string Reason);
public sealed record TokenValidated(string PlayerId, bool IsValid);
