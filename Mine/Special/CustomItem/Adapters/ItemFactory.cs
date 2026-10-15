using System;
using System.Collections.Generic;
using System.Linq;
using CelToon.CustomItem.Contracts;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Definitions;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Definitions;
using UnityEngine;

namespace CelToon.CustomItem.Adapters
{
    public sealed class ItemFactory
    {
        private readonly Dictionary<string, ItemInstance> prototypes = new Dictionary<string, ItemInstance>(StringComparer.Ordinal);
        public ItemFactory(ItemSpecificationCatalog catalog)
        {
            if (catalog == null || catalog.Specifications == null) throw new ArgumentException("物品目录缺失");
            foreach (var spec in catalog.Specifications)
            {
                if (spec == null || !spec.Production) continue;
                var prototype = FromSpecification(spec);
                ValidateStructure(prototype);
                if (prototypes.ContainsKey(spec.SpecificationId)) throw new ArgumentException("重复物品规格：" + spec.SpecificationId);
                prototypes.Add(spec.SpecificationId, prototype);
            }
        }

        public ItemInstance Create(string specificationId)
        {
            if (!prototypes.TryGetValue(specificationId ?? "", out var prototype)) throw new ArgumentException("没有已发布物品规格：" + specificationId);
            var item = Copy(prototype); item.InstanceId = Guid.NewGuid().ToString("N"); return item;
        }

        public bool Validate(ItemInstance item, out string problem)
        {
            try
            {
                ValidateStructure(item);
                if (!prototypes.TryGetValue(item.Content.SpecificationId, out var prototype)) throw new ArgumentException("规格缺失，物品保留但不可装配");
                if (item.DefinitionRef.Family != prototype.DefinitionRef.Family || item.DefinitionRef.PartId != prototype.DefinitionRef.PartId
                    || item.Rarity != prototype.Rarity || JsonUtility.ToJson(item.Content) != JsonUtility.ToJson(prototype.Content)) throw new ArgumentException("规格版本或确定内容不一致，需要明确迁移");
                problem = null; return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NullReferenceException) { problem = ex.Message; return false; }
        }

        public static LauncherResolvedPart ResolveLauncher(ItemInstance item)
        {
            var p = JsonUtility.FromJson<LauncherItemPayload>(item.Content.PayloadJson);
            return new LauncherResolvedPart(LauncherSlots.Id(p.Kind), item.DefinitionRef.PartId, item.DisplayName, p.Kind,
                new ProjectileSpec(p.Damage, p.Speed, p.Radius, p.Lifetime, p.Color), p.EnergyPerProjectile,
                p.Distribution, p.Count, p.SpreadDegrees, p.Operations);
        }

        public static EquipmentResolvedPart ResolveEquipment(ItemInstance item)
        {
            var p = JsonUtility.FromJson<EquipmentItemPayload>(item.Content.PayloadJson);
            return new EquipmentResolvedPart(EquipmentSlots.Id(p.Kind), item.DefinitionRef.PartId, item.DisplayName, p.Kind, p.PrimaryOperations, p.Affixes);
        }

        private static ItemInstance FromSpecification(ItemSpecificationDefinition s)
        {
            return new ItemInstance {
                InstanceId = Guid.NewGuid().ToString("N"), DefinitionRef = new ItemDefinitionRef { Family = s.Family, PartId = s.PartId },
                Rarity = s.Rarity, DisplayName = s.DisplayName, Description = s.Description,
                Content = new ItemContentSnapshot {
                    SpecificationId = s.SpecificationId, SpecificationVersion = s.SpecificationVersion, ContentRevision = s.ContentRevision,
                    GenerationRuleVersion = s.GenerationRuleVersion, PayloadVersion = 1,
                    PayloadFormat = s.Family == ItemFamily.EquipmentCore ? "equipment-core.v1" : "launcher-part.v1",
                    PayloadJson = s.Family == ItemFamily.EquipmentCore ? JsonUtility.ToJson(s.Equipment) : JsonUtility.ToJson(s.Launcher),
                    Affixes = s.Affixes == null ? null : s.Affixes.Select(CopyAffix).ToArray()
                }
            };
        }

        private static ItemAffixSnapshot CopyAffix(ItemAffixSnapshot a) => a == null ? null : JsonUtility.FromJson<ItemAffixSnapshot>(JsonUtility.ToJson(a));
        private static ItemInstance Copy(ItemInstance i) => JsonUtility.FromJson<ItemInstance>(JsonUtility.ToJson(i));

        private static void ValidateStructure(ItemInstance item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.InstanceId) || item.DefinitionRef == null || string.IsNullOrWhiteSpace(item.DefinitionRef.PartId)
                || !Enum.IsDefined(typeof(ItemFamily), item.DefinitionRef.Family) || !Enum.IsDefined(typeof(ItemRarity), item.Rarity)
                || item.Content == null || item.Content.PayloadVersion != 1 || string.IsNullOrWhiteSpace(item.Content.SpecificationId)
                || item.Content.SpecificationVersion < 1 || item.Content.ContentRevision < 1 || item.Content.GenerationRuleVersion < 1
                || string.IsNullOrWhiteSpace(item.Content.PayloadJson) || item.Content.Affixes == null) throw new ArgumentException("物品内容不完整或版本不支持");
            if (item.Rarity == ItemRarity.Legendary) throw new ArgumentException("橙色签名尚未实现，不能生成或装配");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in item.Content.Affixes)
                if (a == null || string.IsNullOrWhiteSpace(a.AffixId) || !ids.Add(a.AffixId) || string.IsNullOrWhiteSpace(a.ParameterPresetId)
                    || a.Role == ItemAffixRole.Signature || a.Parameters == null || a.Parameters.Any(v => !LauncherLimits.Finite(v))
                    || a.SemanticGroupIds == null || a.UniqueGroupIds == null) throw new ArgumentException("词条缺失、重复或签名不支持");
            if (item.DefinitionRef.Family == ItemFamily.EquipmentCore)
            {
                if (item.Content.PayloadFormat != "equipment-core.v1") throw new ArgumentException("装备载荷格式不符");
                var p = JsonUtility.FromJson<EquipmentItemPayload>(item.Content.PayloadJson);
                ItemAffixRules.ValidateEquipment(item.Rarity, p, item.Content.Affixes);
                return;
            }
            if (item.Content.PayloadFormat != "launcher-part.v1") throw new ArgumentException("发射器载荷格式不符");
            var l = JsonUtility.FromJson<LauncherItemPayload>(item.Content.PayloadJson);
            if (!LauncherSlots.Supported(l.Kind) || l.Operations == null || !LauncherLimits.InRange(l.EnergyPerProjectile, 0, 1000000)
                || item.Content.Affixes.Length > (item.Rarity == ItemRarity.Common ? 0 : 1)) throw new ArgumentException("发射器类别、费用或调校数量不符");
            if (l.Kind == PartKind.Projectile)
            {
                if (!new ProjectileSpec(l.Damage, l.Speed, l.Radius, l.Lifetime, l.Color).IsValid || l.Operations.Length != 0) throw new ArgumentException("弹丸参数不符");
            }
            else if (l.Kind == PartKind.Distribution)
            {
                if (!Enum.IsDefined(typeof(DistributionMode), l.Distribution) || l.Count < 1 || l.Count > 128 || (l.Distribution == DistributionMode.Single && l.Count != 1)
                    || !LauncherLimits.InRange(l.SpreadDegrees, 0, 180) || l.Operations.Length != 0 || l.EnergyPerProjectile != 0) throw new ArgumentException("分布参数不符");
            }
            else if (l.Operations.Length < 1 || l.Operations.Length > 8 || l.Operations.Any(s => !s.IsValid || s.Kind != l.Kind)) throw new ArgumentException("发射器调校不能越过类别边界");
            foreach (var a in item.Content.Affixes)
            {
                if (a.Role != ItemAffixRole.Ordinary || a.HighImpact || a.UniqueScope != ItemUniqueScope.LauncherLoadout
                    || !a.SemanticGroupIds.SequenceEqual(new[] { a.AffixId }) || a.UniqueGroupIds.Length != 0) throw new ArgumentException("发射器调校语义组不符");
                if (a.AffixId == "launcher.homing.tuning" && l.Kind == PartKind.Trajectory && a.Parameters.Length == 3 && l.Operations.Length == 1
                    && l.Operations[0].Operation == EffectOperation.Homing && l.Operations[0].A == a.Parameters[0] && l.Operations[0].B == a.Parameters[1] && l.EnergyPerProjectile == a.Parameters[2]) continue;
                if (a.AffixId == "launcher.economy.tuning" && l.Kind == PartKind.Modifier && a.Parameters.Length == 1 && l.Operations.Length == 1
                    && l.Operations[0].Operation == EffectOperation.CostScale && l.Operations[0].A == a.Parameters[0]) continue;
                throw new ArgumentException("没有实现该同类别调校：" + a.AffixId);
            }
        }
    }
}
