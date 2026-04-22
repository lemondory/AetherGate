using AetherGate.Domain.ValueObjects;

namespace AetherGate.Domain.Messages.Events;

// ─── 브로드캐스트용 Zone 이벤트 ──────────────────────────────────────
public sealed record PlayerEnteredZone(string PlayerId, Position Position);
public sealed record PlayerLeftZone(string PlayerId);
public sealed record PlayerMoved(string PlayerId, Position From, Position To);

public sealed record MonsterSpawned(string MonsterId, string TemplateId, Position Position);
public sealed record MonsterMoved(string MonsterId, Position Position);
public sealed record MonsterStateChanged(string MonsterId, Domain.Enums.MonsterState NewState);
public sealed record MonsterDied(string MonsterId, string? KillerPlayerId);

public sealed record CombatDamage(string AttackerId, string TargetId, int Damage, bool IsCritical);
public sealed record SkillUsed(string CasterId, int SkillId, string? TargetId, Position? TargetPosition);
public sealed record BuffApplied(string PlayerId, int SkillId, int DurationMs);

public sealed record ItemDropped(string DropId, int ItemId, int Quantity, Position Position);
public sealed record ItemPickedUp(string PlayerId, string DropId, int ItemId, int Quantity);
public sealed record ItemEnhanced(string PlayerId, int ItemId, int NewLevel, bool Success);

public sealed record GoldChanged(string PlayerId, int Delta, int NewTotal);
public sealed record ChatBroadcast(string SenderName, string Message, bool IsWhisper, string? WhisperTargetId);

public sealed record DungeonInstanceCreated(string ZoneInstanceId, string PlayerId);
public sealed record DungeonCleared(string ZoneInstanceId);
public sealed record DungeonFailed(string ZoneInstanceId);

/// <summary>운영자 전체 공지 — SessionActor가 [SYSTEM] 채팅으로 클라이언트에 전달</summary>
public sealed record AdminNotification(string Message);
