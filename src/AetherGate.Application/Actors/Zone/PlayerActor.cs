using Akka.Actor;
using Akka.Event;
using AetherGate.Domain.Entities;
using AetherGate.Domain.Messages.Commands;
using AetherGate.Domain.Messages.Events;
using AetherGate.Domain.ValueObjects;

namespace AetherGate.Application.Actors.Zone;

/// <summary>
/// 플레이어 한 명의 게임 상태를 관리하는 Actor.
/// - 이동, 스킬 사용, 인벤토리, 골드, 강화 처리
/// - 모든 변경사항은 ZoneActor를 통해 브로드캐스트
/// </summary>
public sealed class PlayerActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _zoneActor;

    // ─── 캐릭터 상태 ───────────────────────────────────────────────────
    public string PlayerId { get; }
    private Position _position;
    private readonly Stats _stats;
    private int _currentHp;
    private int _currentMp;
    private int _gold;

    // 인벤토리: ItemId → InventoryItem
    private readonly Dictionary<int, InventoryItem> _inventory = new();

    // 스킬 쿨다운: SkillId → 마지막 사용 시각
    private readonly Dictionary<int, DateTime> _skillCooldowns = new();

    // 버프 상태
    private int _attackBonus = 0;
    private DateTime _buffExpires = DateTime.MinValue;

    private readonly Random _rng = new();

    // 상점 목록 (데모)
    private static readonly ItemDefinition HealthPotion = new(1, "체력포션", Domain.Enums.ItemType.Consumable, 99, 50);
    private static readonly Dictionary<int, (ItemDefinition Def, int Price)> Shop = new()
    {
        [1] = (HealthPotion, 100)
    };

    public PlayerActor(string playerId, Position spawnPosition, IActorRef zoneActor)
    {
        PlayerId = playerId;
        _position = spawnPosition;
        _zoneActor = zoneActor;
        _stats = Stats.Default;
        _currentHp = _stats.MaxHp;
        _currentMp = _stats.MaxMp;
        _gold = 1000; // 초기 골드

        Receive<MoveCommand>(HandleMove);
        Receive<UseSkillCommand>(HandleSkill);
        Receive<PickupItemCommand>(HandlePickup);
        Receive<UseItemCommand>(HandleUseItem);
        Receive<EnhanceItemCommand>(HandleEnhance);
        Receive<BuyItemCommand>(HandleBuy);
        Receive<CombatDamage>(HandleDamage);
        Receive<MonsterDied>(HandleMonsterDied);
    }

    private void HandleMove(MoveCommand cmd)
    {
        var from = _position;
        // 이동 속도 기반 실제 이동 (틱 단위 처리 방식 생략 — 목적지 직접 이동 데모)
        _position = cmd.Destination;

        _zoneActor.Tell(new PlayerMoved(PlayerId, from, _position));
        _log.Debug("[Player:{0}] Moved {1} → {2}", PlayerId, from, _position);
    }

    private void HandleSkill(UseSkillCommand cmd)
    {
        var skillDef = cmd.SkillId switch
        {
            1 => SkillCatalog.SlashAttack,
            2 => SkillCatalog.Whirlwind,
            3 => SkillCatalog.BattleCry,
            _ => null
        };

        if (skillDef is null)
        {
            _log.Warning("[Player:{0}] Unknown skill {1}", PlayerId, cmd.SkillId);
            return;
        }

        // 쿨다운 체크
        if (_skillCooldowns.TryGetValue(cmd.SkillId, out var lastUsed) &&
            (DateTime.UtcNow - lastUsed).TotalMilliseconds < skillDef.CooldownMs)
        {
            _log.Debug("[Player:{0}] Skill {1} is on cooldown", PlayerId, skillDef.Name);
            return;
        }

        // MP 체크
        if (_currentMp < skillDef.MpCost)
        {
            _log.Debug("[Player:{0}] Not enough MP for {1}", PlayerId, skillDef.Name);
            return;
        }

        _currentMp -= skillDef.MpCost;
        _skillCooldowns[cmd.SkillId] = DateTime.UtcNow;

        _zoneActor.Tell(new SkillUsed(PlayerId, cmd.SkillId, cmd.TargetId, cmd.TargetPosition));

        switch (skillDef.Type)
        {
            case Domain.Enums.SkillType.SingleAttack:
                if (cmd.TargetId is not null)
                {
                    int dmg = (_stats.Attack + _attackBonus + skillDef.Damage);
                    bool crit = _rng.NextSingle() < 0.15f;
                    if (crit) dmg = (int)(dmg * 1.5f);
                    _zoneActor.Tell(new CombatDamage(PlayerId, cmd.TargetId, dmg, crit));
                }
                break;

            case Domain.Enums.SkillType.AreaAttack:
                // ZoneActor가 범위 내 타겟 판별 후 CombatDamage 전송 (간략화)
                _zoneActor.Tell(new AreaSkillRequest(PlayerId, skillDef, cmd.TargetPosition ?? _position));
                break;

            case Domain.Enums.SkillType.Buff:
                _attackBonus = skillDef.BuffAttackBonus;
                _buffExpires = DateTime.UtcNow.AddMilliseconds(skillDef.BuffDurationMs);
                _zoneActor.Tell(new BuffApplied(PlayerId, cmd.SkillId, skillDef.BuffDurationMs));
                break;
        }
    }

    private void HandlePickup(PickupItemCommand cmd)
    {
        // ZoneActor에서 드롭 확인 후 처리 (여기서는 간략화)
        _log.Debug("[Player:{0}] Picking up drop: {1}", PlayerId, cmd.DropId);
    }

    private void HandleUseItem(UseItemCommand cmd)
    {
        if (!_inventory.TryGetValue(cmd.ItemId, out var item)) return;
        if (item.Definition.Type != Domain.Enums.ItemType.Consumable) return;

        if (item.TryConsume(1))
        {
            // 체력포션
            if (cmd.ItemId == 1)
            {
                int heal = 50;
                _currentHp = Math.Min(_currentHp + heal, _stats.MaxHp);
                _log.Info("[Player:{0}] Used potion, HP={1}/{2}", PlayerId, _currentHp, _stats.MaxHp);
            }
        }
    }

    private void HandleEnhance(EnhanceItemCommand cmd)
    {
        if (!_inventory.TryGetValue(cmd.ItemId, out var item)) return;

        bool success = item.TryEnhance(_rng);
        _log.Info("[Player:{0}] Enhance item {1}: {2} (lv={3})",
            PlayerId, cmd.ItemId, success ? "SUCCESS" : "FAIL", item.EnhanceLevel);

        _zoneActor.Tell(new ItemEnhanced(PlayerId, cmd.ItemId, item.EnhanceLevel, success));
    }

    private void HandleBuy(BuyItemCommand cmd)
    {
        if (!Shop.TryGetValue(cmd.ItemId, out var entry)) return;

        int totalCost = entry.Price * cmd.Quantity;
        if (_gold < totalCost)
        {
            _log.Debug("[Player:{0}] Not enough gold. Have={1}, Need={2}", PlayerId, _gold, totalCost);
            return;
        }

        _gold -= totalCost;
        AddToInventory(entry.Def, cmd.Quantity);
        _zoneActor.Tell(new GoldChanged(PlayerId, -totalCost, _gold));
        _log.Info("[Player:{0}] Bought {1}x{2}, gold={3}", PlayerId, cmd.Quantity, entry.Def.Name, _gold);
    }

    private void HandleDamage(CombatDamage evt)
    {
        if (evt.TargetId != PlayerId) return;

        _currentHp -= evt.Damage;
        _log.Info("[Player:{0}] Took {1}{2} damage. HP={3}/{4}",
            PlayerId, evt.Damage, evt.IsCritical ? "(CRIT)" : "", _currentHp, _stats.MaxHp);

        if (_currentHp <= 0)
        {
            _currentHp = 0;
            _log.Info("[Player:{0}] Died.", PlayerId);
            // 부활 처리는 별도 (세션 레벨에서 처리)
        }
    }

    private void HandleMonsterDied(MonsterDied evt)
    {
        if (evt.KillerPlayerId != PlayerId) return;

        // 골드 드롭 (랜덤)
        int goldDrop = _rng.Next(5, 50);
        _gold += goldDrop;
        _zoneActor.Tell(new GoldChanged(PlayerId, goldDrop, _gold));
        _log.Info("[Player:{0}] Got {1} gold from monster {2}", PlayerId, goldDrop, evt.MonsterId);
    }

    private void AddToInventory(ItemDefinition def, int quantity)
    {
        if (_inventory.TryGetValue(def.Id, out var existing))
            existing.Add(quantity);
        else
            _inventory[def.Id] = new InventoryItem(def, quantity);
    }

    // ZoneActor 내부에서만 사용되는 AoE 요청 메시지
    public sealed record AreaSkillRequest(
        string CasterId,
        SkillDefinition Skill,
        Position Center);
}
