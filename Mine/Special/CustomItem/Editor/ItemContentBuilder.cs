using System;
using System.Collections.Generic;
using System.Linq;
using CelToon.CustomItem.Adapters;
using CelToon.CustomItem.Contracts;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomLauncher.Contracts;
using UnityEditor;
using UnityEngine;

namespace CelToon.CustomItem.Editor
{
    public static class ItemContentBuilder
    {
        public const string Root = "Assets/Mine/Special/CustomItem/Samples/Definitions";
        public const string CatalogPath = Root + "/ItemSampleCatalog.asset";

        [MenuItem("CelToon/Custom Item/Build Sample Specifications")]
        public static void BuildSamples() => Build();

        public static ItemSpecificationCatalog Build()
        {
            EnsureFolder(Root);
            var specifications = new List<ItemSpecificationDefinition>();
            specifications.Add(Equipment("ShieldWhite", "item.shield.common", "equipment.resource.shieldcell", "常规护盾 / 白", ItemRarity.Common,
                EquipmentKind.ShieldCore, 40, "护盾 +40；回复 3/s，受伤后等待 3s。固定基础回复占一条。",
                Baseline(ItemAffixRules.Equipment("equipment.shield.regen", "shield.basic", 3, 3))));
            specifications.Add(Equipment("HealthGreen", "item.health.uncommon", "equipment.resource.armoredhealth", "轻甲生命 / 绿", ItemRarity.Uncommon,
                EquipmentKind.HealthCore, 40, "生命 +40；护甲 +6。护甲只平减直接伤害。",
                ItemAffixRules.Equipment("equipment.armor", "armor.light", 6)));
            specifications.Add(Equipment("ShieldBlue", "item.shield.rare", "equipment.resource.shieldcell", "常规护盾 / 蓝", ItemRarity.Rare,
                EquipmentKind.ShieldCore, 50, "护盾 +50；回复 12/s，等待 2s；燃烧抗性 15%。回复与等待作为一条。",
                ItemAffixRules.Equipment("equipment.shield.regen", "shield.rare", 12, 2), ItemAffixRules.Equipment("equipment.resistance.burn", "burn.rare", .15f)));
            specifications.Add(Equipment("ShieldPurple", "item.shield.epic", "equipment.resource.shieldcell", "常规护盾 / 紫", ItemRarity.Epic,
                EquipmentKind.ShieldCore, 50, "护盾 +50；回复 16/s，等待 1.5s；燃烧抗性 25%。与蓝色同为两条。",
                ItemAffixRules.Equipment("equipment.shield.regen", "shield.epic", 16, 1.5f), ItemAffixRules.Equipment("equipment.resistance.burn", "burn.epic", .25f)));
            specifications.Add(Equipment("StaminaBlue", "item.stamina.rare", "equipment.resource.balancedstamina", "机动体力 / 蓝", ItemRarity.Rare,
                EquipmentKind.StaminaCore, 30, "体力 +30；步行速度 ×1.2；体力回复 +8/s。两条独立副词条。",
                ItemAffixRules.Equipment("equipment.move.scale", "move.rare", 1.2f), ItemAffixRules.Equipment("equipment.stamina.regen", "stamina.rare", 8)));
            specifications.Add(Equipment("EnergyPurple", "item.energy.epic", "equipment.resource.efficientenergy", "节能能量 / 紫", ItemRarity.Epic,
                EquipmentKind.EnergyCore, 40, "能量 +40；能量回复 +8/s；人物能量消耗 ×0.65。可与发射器节能共同生效。",
                ItemAffixRules.Equipment("equipment.energy.regen", "energy.epic", 8), ItemAffixRules.Equipment("equipment.energy.cost", "cost.epic", .65f)));
            var projectile = Spec("ProjectileWhite", "item.projectile.common", "projectile.standard", "标准弹 / 白", ItemFamily.LauncherPart, ItemRarity.Common,
                "伤害 12，弹速 38m/s，半径 0.09m，寿命 3s，每弹费用 1。基础弹丸，无额外调校。");
            projectile.Launcher = new LauncherItemPayload { Kind = PartKind.Projectile, Damage = 12, Speed = 38, Radius = .09f, Lifetime = 3,
                EnergyPerProjectile = 1, Color = new Color(.1f, .8f, 1) };
            specifications.Add(projectile); Save(projectile);
            var homing = Spec("HomingGreen", "item.homing.uncommon", "trajectory.Homing", "追踪调校 / 绿", ItemFamily.LauncherPart, ItemRarity.Uncommon,
                "追踪转向 4，搜索范围 28m，每弹附加费用 1.4。只有一条同类别调校，不附送伤害效果。");
            homing.Launcher = new LauncherItemPayload { Kind = PartKind.Trajectory, EnergyPerProjectile = 1.4f, Operations = new[] { new EffectStep(EffectOperation.Homing, 4, 28) } };
            homing.Affixes = new[] { LauncherAffix("launcher.homing.tuning", "homing.green", 4, 28, 1.4f) };
            specifications.Add(homing); Save(homing);
            var economy = Spec("EconomyBlue", "item.economy.rare", "modifier.Economy", "节能调校 / 蓝", ItemFamily.LauncherPart, ItemRarity.Rare,
                "发射器费用 ×0.6。与人物节能 ×0.65 组合时实际费用 ×0.39；联合测试持续输出。");
            economy.Launcher = new LauncherItemPayload { Kind = PartKind.Modifier, Operations = new[] { new EffectStep(EffectOperation.CostScale, .6f) } };
            economy.Affixes = new[] { LauncherAffix("launcher.economy.tuning", "economy.blue", .6f) };
            specifications.Add(economy); Save(economy);

            var catalog = Asset<ItemSpecificationCatalog>(Root + "/ItemSampleCatalog.asset");
            catalog.Specifications = specifications.ToArray(); catalog.StarterSpecificationId = "item.projectile.common";
            catalog.WorldSamples = specifications.Select((s, i) => new ItemWorldSample {
                SpawnId = "sample." + i, SpecificationId = s.SpecificationId,
                Position = new Vector3((i % 3 - 1) * 1.5f, 0, 1.4f + i / 3 * 1.8f)
            }).ToArray();
            new ItemFactory(catalog); // Content errors stop publication before scene creation.
            Save(catalog); AssetDatabase.SaveAssets(); return catalog;
        }

        private static ItemSpecificationDefinition Equipment(string name, string id, string partId, string title, ItemRarity rarity,
            EquipmentKind kind, float capacity, string description, params ItemAffixSnapshot[] affixes)
        {
            var spec = Spec(name, id, partId, title, ItemFamily.EquipmentCore, rarity, description);
            spec.Affixes = affixes;
            spec.Equipment = new EquipmentItemPayload { Kind = kind,
                PrimaryOperations = new[] { new EquipmentStep(EquipmentSlots.PrimaryOperation(kind), capacity) },
                Affixes = affixes.SelectMany(ItemAffixRules.ExpandEquipment).ToArray() };
            Save(spec); return spec;
        }
        private static ItemAffixSnapshot Baseline(ItemAffixSnapshot a) { a.Role = ItemAffixRole.Baseline; return a; }
        private static ItemAffixSnapshot LauncherAffix(string id, string preset, params float[] values) => new ItemAffixSnapshot {
            AffixId = id, ParameterPresetId = preset, Role = ItemAffixRole.Ordinary, Parameters = values,
            SemanticGroupIds = new[] { id }, UniqueGroupIds = Array.Empty<string>(), UniqueScope = ItemUniqueScope.LauncherLoadout
        };
        private static ItemSpecificationDefinition Spec(string name, string id, string partId, string title, ItemFamily family, ItemRarity rarity, string description)
        {
            var s = Asset<ItemSpecificationDefinition>(Root + "/" + name + ".asset");
            s.SpecificationId = id; s.PartId = partId; s.DisplayName = title; s.Description = description;
            s.Family = family; s.Rarity = rarity; s.Production = true;
            s.SpecificationVersion = s.ContentRevision = s.GenerationRuleVersion = 1;
            s.Affixes = Array.Empty<ItemAffixSnapshot>(); s.Launcher = new LauncherItemPayload(); s.Equipment = new EquipmentItemPayload();
            return s;
        }
        private static T Asset<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset;
        }
        private static void Save(UnityEngine.Object asset) => EditorUtility.SetDirty(asset);
        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/'); string parent = parts[0];
            for (int i = 1; i < parts.Length; i++) { string next = parent + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, parts[i]); parent = next; }
        }
    }
}
