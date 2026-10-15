using System;
using System.Collections.Generic;
using System.Linq;
using CelToon.CustomItem.Contracts;
using CelToon.CustomEquipment.Contracts;
using UnityEngine;

namespace CelToon.CustomItem.Adapters
{
    // Stable semantic IDs are distinct from executable operation arrays.
    public static class ItemAffixRules
    {
        public static ItemAffixSnapshot Equipment(string id, string preset, params float[] parameters)
        {
            Describe(id, out _, out _, out bool unique, out bool high);
            return new ItemAffixSnapshot {
                AffixId = id, ParameterPresetId = preset, Role = ItemAffixRole.Ordinary,
                Parameters = parameters, SemanticGroupIds = new[] { id },
                UniqueGroupIds = unique ? new[] { id } : Array.Empty<string>(),
                UniqueScope = ItemUniqueScope.EquipmentLoadout, HighImpact = high
            };
        }

        public static EquipmentStep[] ExpandEquipment(ItemAffixSnapshot affix)
        {
            var p = affix.Parameters ?? Array.Empty<float>();
            Action<int> require = n => { if (p.Length != n || p.Any(v => !EquipmentLimits.Range(v, 0, 1000))) throw new ArgumentException("词条参数不合法：" + affix.AffixId); };
            switch (affix.AffixId)
            {
                case "equipment.shield.regen": require(2); return new[] { new EquipmentStep(EquipmentOperation.ShieldRegen, p[0]), new EquipmentStep(EquipmentOperation.ShieldDelay, p[1]) };
                case "equipment.health.regen": require(2); return new[] { new EquipmentStep(EquipmentOperation.HealthRegen, p[0]), new EquipmentStep(EquipmentOperation.RecoveryDelay, p[1]) };
                case "equipment.health.emergency": require(2); return new[] { new EquipmentStep(EquipmentOperation.EmergencyHeal, p[0], p[1]) };
                default:
                    require(1);
                    var operations = new Dictionary<string, EquipmentOperation> {
                        {"equipment.armor", EquipmentOperation.ArmorAdd}, {"equipment.damage.scale", EquipmentOperation.DamageScale},
                        {"equipment.resistance.burn", EquipmentOperation.BurnResistance}, {"equipment.resistance.poison", EquipmentOperation.PoisonResistance},
                        {"equipment.resistance.slow", EquipmentOperation.SlowResistance}, {"equipment.resistance.stun", EquipmentOperation.StunResistance},
                        {"equipment.resistance.mark", EquipmentOperation.MarkResistance}, {"equipment.resistance.impulse", EquipmentOperation.ImpulseResistance},
                        {"equipment.stamina.regen", EquipmentOperation.StaminaRegen}, {"equipment.energy.regen", EquipmentOperation.EnergyRegen},
                        {"equipment.move.scale", EquipmentOperation.MoveScale}, {"equipment.sprint.scale", EquipmentOperation.SprintScale},
                        {"equipment.sprint.cost", EquipmentOperation.SprintCostScale}, {"equipment.energy.cost", EquipmentOperation.EnergyCostScale},
                        {"equipment.hit.protection", EquipmentOperation.HitInvulnerability}, {"equipment.cleanse", EquipmentOperation.CleanseInterval}
                    };
                    if (!operations.TryGetValue(affix.AffixId ?? "", out var operation)) throw new ArgumentException("未知词条：" + affix.AffixId);
                    return new[] { new EquipmentStep(operation, p[0]) };
            }
        }

        public static void ValidateEquipment(ItemRarity rarity, EquipmentItemPayload payload, ItemAffixSnapshot[] affixes)
        {
            if (!EquipmentSlots.Supported(payload.Kind) || payload.PrimaryOperations == null || payload.PrimaryOperations.Length == 0
                || payload.PrimaryOperations.Any(s => !EquipmentSlots.AcceptsPrimary(payload.Kind, s))) throw new ArgumentException("主属性必须对应本槽资源上限");
            int expected = rarity == ItemRarity.Common ? -1 : rarity == ItemRarity.Uncommon ? 1 : 2;
            if (affixes.Length > 2 || (expected >= 0 && affixes.Length != expected)) throw new ArgumentException("装备副词条数量与品级规格不符");
            if (rarity == ItemRarity.Common && (affixes.Length > 1 || affixes.Any(a => payload.Kind != EquipmentKind.ShieldCore || a.AffixId != "equipment.shield.regen" || a.Role != ItemAffixRole.Baseline)))
                throw new ArgumentException("白色仅允许护盾固定基础回复");
            if (affixes.Count(a => a.HighImpact) > 1) throw new ArgumentException("每件核心最多一个高价值功能");
            var expanded = new List<EquipmentStep>();
            foreach (var a in affixes)
            {
                Describe(a.AffixId, out var kinds, out var minimum, out bool unique, out bool high);
                if (!kinds.Contains(payload.Kind) || rarity < minimum || a.HighImpact != high || a.UniqueScope != ItemUniqueScope.EquipmentLoadout
                    || !a.SemanticGroupIds.SequenceEqual(new[] { a.AffixId })
                    || !a.UniqueGroupIds.SequenceEqual(unique ? new[] { a.AffixId } : Array.Empty<string>())) throw new ArgumentException("词条资格或效果组不符：" + a.AffixId);
                if (rarity != ItemRarity.Common && a.Role != ItemAffixRole.Ordinary) throw new ArgumentException("当前规格只支持普通词条");
                expanded.AddRange(ExpandEquipment(a));
            }
            if (payload.Kind == EquipmentKind.ShieldCore && !affixes.Any(a => a.AffixId == "equipment.shield.regen")) throw new ArgumentException("生产护盾需要可用回复机制");
            if (payload.Affixes == null || !Same(expanded.ToArray(), payload.Affixes) || payload.Affixes.Any(s => !EquipmentSlots.AcceptsAffix(s))
                || payload.PrimaryOperations.Length + payload.Affixes.Length > EquipmentSlots.MaxOperationsPerPart) throw new ArgumentException("语义词条与确定执行效果不一致");
        }

        private static bool Same(EquipmentStep[] a, EquipmentStep[] b) => a.Length == b.Length && a.Zip(b, (x, y) => x.Operation == y.Operation && x.A == y.A && x.B == y.B).All(x => x);

        private static void Describe(string id, out EquipmentKind[] kinds, out ItemRarity minimum, out bool unique, out bool high)
        {
            var h = EquipmentKind.HealthCore; var s = EquipmentKind.ShieldCore; var t = EquipmentKind.StaminaCore; var e = EquipmentKind.EnergyCore;
            minimum = ItemRarity.Uncommon; unique = false; high = false;
            switch (id)
            {
                case "equipment.shield.regen": kinds = new[] { s }; minimum = ItemRarity.Common; unique = true; break;
                case "equipment.armor": case "equipment.resistance.burn": case "equipment.resistance.poison": case "equipment.resistance.mark": kinds = new[] { h, s }; break;
                case "equipment.resistance.slow": case "equipment.resistance.stun": kinds = new[] { h, s, t }; break;
                case "equipment.resistance.impulse": kinds = new[] { t }; break;
                case "equipment.stamina.regen": case "equipment.energy.regen": kinds = new[] { t, e }; break;
                case "equipment.move.scale": case "equipment.sprint.scale": kinds = new[] { t }; unique = true; break;
                case "equipment.sprint.cost": kinds = new[] { t }; minimum = ItemRarity.Rare; unique = true; break;
                case "equipment.energy.cost": kinds = new[] { e }; minimum = ItemRarity.Rare; unique = true; break;
                case "equipment.health.regen": kinds = new[] { h }; minimum = ItemRarity.Rare; unique = true; break;
                case "equipment.health.emergency": kinds = new[] { h }; minimum = ItemRarity.Rare; unique = high = true; break;
                case "equipment.damage.scale": case "equipment.hit.protection": kinds = new[] { h, s }; minimum = ItemRarity.Epic; unique = high = true; break;
                case "equipment.cleanse": kinds = new[] { h, e }; minimum = ItemRarity.Epic; unique = high = true; break;
                default: throw new ArgumentException("没有审核的装备词条：" + id);
            }
        }
    }
}
