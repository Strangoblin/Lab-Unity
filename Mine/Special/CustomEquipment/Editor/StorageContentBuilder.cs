using System;
using System.Collections.Generic;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Definitions;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Definitions;
using UnityEditor;
using UnityEngine;

namespace CelToon.CustomEquipment.Editor
{
    public static class StorageContentBuilder
    {
        private const string Path = StorageSceneBuilder.RootPath + "/Samples/Definitions/";
        private static readonly List<EquipmentPartDefinition> parts = new List<EquipmentPartDefinition>();
        private static readonly Dictionary<string, EquipmentPartDefinition> byId = new Dictionary<string, EquipmentPartDefinition>();
        private static EquipmentStep E(EquipmentOperation op, float a, float b = 0) => new EquipmentStep(op, a, b);
        public static EquipmentCatalog Build()
        {
            StorageSceneBuilder.EnsureFolder(Path.TrimEnd('/')); parts.Clear(); byId.Clear();
            P("Resource_BalancedHealth", "均衡生命核心", EquipmentKind.HealthCore, "主属性：生命 +40。副词条：护甲 6、伤害 ×0.85、五类抗性 20%、脱战回复 3/s。", E(EquipmentOperation.HealthAdd, 40), E(EquipmentOperation.ArmorAdd, 6), E(EquipmentOperation.DamageScale, .85f), E(EquipmentOperation.BurnResistance, .2f), E(EquipmentOperation.PoisonResistance, .2f), E(EquipmentOperation.SlowResistance, .2f), E(EquipmentOperation.StunResistance, .2f), E(EquipmentOperation.MarkResistance, .2f), E(EquipmentOperation.HealthRegen, 3));
            P("Resource_ArmoredHealth", "重甲生命核心", EquipmentKind.HealthCore, "主属性：生命 +50。副词条：护甲 16、伤害 ×0.9、受击保护 0.3 秒。", E(EquipmentOperation.HealthAdd, 50), E(EquipmentOperation.ArmorAdd, 16), E(EquipmentOperation.DamageScale, .9f), E(EquipmentOperation.HitInvulnerability, .3f));
            P("Resource_ResistantHealth", "隔离生命核心", EquipmentKind.HealthCore, "主属性：生命 +20。副词条：护甲 4、燃烧/中毒抗性 65%、减速/眩晕 40%、易伤 50%。", E(EquipmentOperation.HealthAdd, 20), E(EquipmentOperation.ArmorAdd, 4), E(EquipmentOperation.BurnResistance, .65f), E(EquipmentOperation.PoisonResistance, .65f), E(EquipmentOperation.SlowResistance, .4f), E(EquipmentOperation.StunResistance, .4f), E(EquipmentOperation.MarkResistance, .5f));
            P("Resource_VitalHealth", "急救生命核心", EquipmentKind.HealthCore, "主属性：生命上限 ×2。副词条：低生命治疗 20%、冷却 12 秒；不能复活。", E(EquipmentOperation.HealthScale, 2), E(EquipmentOperation.EmergencyHeal, .2f, 12));

            P("Resource_ShieldCell", "常规护盾核心", EquipmentKind.ShieldCore, "主属性：护盾 +50。副词条：回复 8/s、受伤后等待 3 秒。", E(EquipmentOperation.ShieldAdd, 50), E(EquipmentOperation.ShieldRegen, 8), E(EquipmentOperation.ShieldDelay, 3));
            P("Resource_HeavyShield", "重型护盾核心", EquipmentKind.ShieldCore, "主属性：护盾 +120。副词条：回复 10/s、受伤后等待 3 秒。", E(EquipmentOperation.ShieldAdd, 120), E(EquipmentOperation.ShieldRegen, 10), E(EquipmentOperation.ShieldDelay, 3));
            P("Resource_QuickShield", "快充护盾核心", EquipmentKind.ShieldCore, "主属性：护盾 +40。副词条：回复 20/s、受伤后等待 1 秒。", E(EquipmentOperation.ShieldAdd, 40), E(EquipmentOperation.ShieldRegen, 20), E(EquipmentOperation.ShieldDelay, 1));
            P("Resource_ReserveShield", "储备护盾核心", EquipmentKind.ShieldCore, "主属性：护盾 +200。无回复副词条，依靠显式重置补充。", E(EquipmentOperation.ShieldAdd, 200));

            P("Resource_BalancedStamina", "均衡体力核心", EquipmentKind.StaminaCore, "主属性：体力 +40。副词条：步行 ×1.2、击退抗性 40%、回复 +8/s、冲刺消耗 ×0.75。", E(EquipmentOperation.StaminaAdd, 40), E(EquipmentOperation.MoveScale, 1.2f), E(EquipmentOperation.ImpulseResistance, .4f), E(EquipmentOperation.StaminaRegen, 8), E(EquipmentOperation.SprintCostScale, .75f));
            P("Resource_SprintStamina", "疾跑体力核心", EquipmentKind.StaminaCore, "主属性：体力 +20。副词条：步行 ×1.5、冲刺倍率 ×1.2、冲刺消耗 ×1.2。", E(EquipmentOperation.StaminaAdd, 20), E(EquipmentOperation.MoveScale, 1.5f), E(EquipmentOperation.SprintScale, 1.2f), E(EquipmentOperation.SprintCostScale, 1.2f));
            P("Resource_StableStamina", "稳定体力核心", EquipmentKind.StaminaCore, "主属性：体力 +30。副词条：击退抗性 80%、减速/眩晕抗性 40%。", E(EquipmentOperation.StaminaAdd, 30), E(EquipmentOperation.ImpulseResistance, .8f), E(EquipmentOperation.SlowResistance, .4f), E(EquipmentOperation.StunResistance, .4f));
            P("Resource_EnduranceStamina", "耐力体力核心", EquipmentKind.StaminaCore, "主属性：体力 +60。副词条：步行 ×1.1、回复 +15/s、冲刺消耗 ×0.5。", E(EquipmentOperation.StaminaAdd, 60), E(EquipmentOperation.MoveScale, 1.1f), E(EquipmentOperation.StaminaRegen, 15), E(EquipmentOperation.SprintCostScale, .5f));

            P("Resource_BalancedEnergy", "均衡能量核心", EquipmentKind.EnergyCore, "主属性：能量 +40。副词条：能量回复 +4/s。", E(EquipmentOperation.EnergyAdd, 40), E(EquipmentOperation.EnergyRegen, 4));
            P("Resource_ReserveEnergy", "储备能量核心", EquipmentKind.EnergyCore, "主属性：能量 +150。副词条：能量消耗 ×1.15，容量与消耗取舍。", E(EquipmentOperation.EnergyAdd, 150), E(EquipmentOperation.EnergyCostScale, 1.15f));
            P("Resource_QuickEnergy", "快充能量核心", EquipmentKind.EnergyCore, "主属性：能量 +20。副词条：能量回复 +20/s。", E(EquipmentOperation.EnergyAdd, 20), E(EquipmentOperation.EnergyRegen, 20));
            P("Resource_EfficientEnergy", "节能能量核心", EquipmentKind.EnergyCore, "主属性：能量 +30。副词条：能量消耗 ×0.65、每 5 秒净化持续状态。", E(EquipmentOperation.EnergyAdd, 30), E(EquipmentOperation.EnergyCostScale, .65f), E(EquipmentOperation.CleanseInterval, 5));

            var recipes = new[] {
                R("Custom", "自定义 / 空配方", "标准本体，无配件；生命、护盾、体力、能量四个槽位全部开放。"),
                R("Balanced", "均衡防护", "生命、护盾、体力与能量均衡配置。", "Resource_BalancedHealth", "Resource_ShieldCell", "Resource_BalancedStamina", "Resource_BalancedEnergy"),
                R("Fortress", "移动堡垒", "重甲生命、重型护盾、稳定体力与储备能量。", "Resource_ArmoredHealth", "Resource_HeavyShield", "Resource_StableStamina", "Resource_ReserveEnergy"),
                R("Runner", "疾行者", "均衡生命、快充护盾、疾跑体力与快充能量。", "Resource_BalancedHealth", "Resource_QuickShield", "Resource_SprintStamina", "Resource_QuickEnergy"),
                R("Hazmat", "火毒隔离", "状态抗性与周期净化，便于对比持续伤害。", "Resource_ResistantHealth", "Resource_ShieldCell", "Resource_EnduranceStamina", "Resource_EfficientEnergy"),
                R("Shield", "护盾循环", "快充护盾与生命修复，配有体力和能量核心。", "Resource_BalancedHealth", "Resource_QuickShield", "Resource_BalancedStamina", "Resource_BalancedEnergy"),
                R("Rescue", "应急恢复", "急救生命核心与快充资源，观察低生命冷却。", "Resource_VitalHealth", "Resource_QuickShield", "Resource_EnduranceStamina", "Resource_QuickEnergy"),
                R("Composite", "复合配件", "四种资源主属性，附带多种固定副词条。", "Resource_BalancedHealth", "Resource_ShieldCell", "Resource_BalancedStamina", "Resource_ReserveEnergy"),
                R("HealthOnly", "纯生命对照", "仅装备急救生命核心，比较生命与急救。", "Resource_VitalHealth"),
                R("ArmorOnly", "重甲生命对照", "仅装备重甲生命核心，比较平减与受击保护。", "Resource_ArmoredHealth")
            };
            var catalog = Asset<EquipmentCatalog>(Path + "Catalog.asset", x => { x.Parts = parts.ToArray(); x.Recipes = recipes; });
            catalog.Parts = parts.ToArray(); catalog.Recipes = recipes; EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog); return catalog;
        }
        private static void P(string id, string name, EquipmentKind kind, string description, params EquipmentStep[] operations)
        {
            var part = Asset<EquipmentPartDefinition>(Path + id + ".asset", x => {
                x.PartId = "equipment.resource." + id.Substring(9).ToLowerInvariant(); x.DisplayName = name; x.Kind = kind; x.Description = description;
                x.PrimaryOperations = Array.FindAll(operations, step => EquipmentSlots.IsPrimary(step.Operation));
                x.Affixes = Array.FindAll(operations, step => !EquipmentSlots.IsPrimary(step.Operation));
            });
            parts.Add(part); byId.Add(id, part);
        }
        private static EquipmentRecipeDefinition R(string id, string name, string description, params string[] equipped)
        {
            var recipe = Asset<EquipmentRecipeDefinition>(Path + "Recipe_" + id + ".asset", x => {
                x.RecipeId = "equipment.recipe." + id.ToLowerInvariant(); x.DisplayName = name; x.Description = description;
                x.BaseStats = EquipmentStats.Standard; x.ResourceSchemaVersion = 1; x.Parts = Array.ConvertAll(equipped, key => byId[key]);
            });
            EquipmentCoreMigration.UpgradeRecipe(recipe);
            if (id == "Custom" && recipe.Description != null && recipe.Description.Contains("八个槽位")) {
                recipe.Description = recipe.Description.Replace("八个槽位", "四个核心槽位"); EditorUtility.SetDirty(recipe); AssetDatabase.SaveAssetIfDirty(recipe);
            }
            return recipe;
        }
        public static LauncherRecipeDefinition[] BuildThreats()
        {
            string path = StorageSceneBuilder.RootPath + "/Samples/Threats/"; StorageSceneBuilder.EnsureFolder(path.TrimEnd('/'));
            var recipes = new List<LauncherRecipeDefinition>();
            Threat(path, recipes, "Kinetic", "标准动能", "直接伤害 25，无附加状态。", 25, Color.yellow);
            Threat(path, recipes, "Heavy", "重击与击退", "直接伤害 60，击退速度 10。", 60, new Color(1, .65f, .2f), new EffectStep(EffectOperation.Push, 10));
            Threat(path, recipes, "Burn", "燃烧弹", "直接伤害 10 + 燃烧 8/s，持续 4 秒。", 10, new Color(1, .3f, .05f), new EffectStep(EffectOperation.Burn, 8, 4));
            Threat(path, recipes, "Poison", "中毒弹", "直接伤害 8 + 中毒 5/s，持续 7 秒。", 8, Color.green, new EffectStep(EffectOperation.Poison, 5, 7));
            Threat(path, recipes, "Slow", "寒霜减速", "直接伤害 12，移动降至 30%，持续 4 秒。", 12, Color.cyan, new EffectStep(EffectOperation.Slow, .3f, 4));
            Threat(path, recipes, "Stun", "震击眩晕", "直接伤害 16，眩晕 1.2 秒。", 16, Color.magenta, new EffectStep(EffectOperation.Stun, 1, 1.2f));
            Threat(path, recipes, "Mark", "易伤标记", "直接伤害 10，后续伤害 ×1.5，持续 5 秒。", 10, new Color(.8f, .4f, 1), new EffectStep(EffectOperation.Mark, 1.5f, 5));
            Threat(path, recipes, "FirePoison", "火毒复合弹", "直接伤害 12，燃烧 6/s ×4 秒 + 中毒 4/s ×6 秒。", 12, new Color(.8f, 1, .2f), new EffectStep(EffectOperation.Burn, 6, 4), new EffectStep(EffectOperation.Poison, 4, 6));
            AssetDatabase.SaveAssets(); return recipes.ToArray();
        }
        private static void Threat(string path, List<LauncherRecipeDefinition> recipes, string id, string name, string description, float damage, Color color, params EffectStep[] steps)
        {
            var projectile = Asset<ProjectileDefinition>(path + id + "_Projectile.asset", x => {
                x.PartId = "storage.projectile." + id; x.DisplayName = name; x.Description = description;
                x.Damage = damage; x.Speed = 12; x.Radius = .12f; x.Lifetime = 5; x.Color = color; x.EnergyPerProjectile = 0;
            });
            var equipped = new List<PartDefinition> { projectile };
            if (steps.Length > 0) equipped.Add(Asset<EffectPartDefinition>(path + id + "_Effect.asset", x => {
                x.PartId = "storage.effect." + id; x.DisplayName = name; x.Description = description;
                x.Category = steps[0].Kind; x.Operations = steps; x.EnergyPerProjectile = 0;
            }));
            recipes.Add(Asset<LauncherRecipeDefinition>(path + id + "_Recipe.asset", x => {
                x.RecipeId = "storage.threat." + id; x.DisplayName = name; x.Description = description;
                x.Cooldown = .35f; x.BaseEnergyCost = 0; x.Parts = equipped.ToArray();
            }));
        }
        private static T Asset<T>(string path, Action<T> initialize) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path); if (existing != null) return existing;
            var asset = ScriptableObject.CreateInstance<T>(); initialize(asset); AssetDatabase.CreateAsset(asset, path); return asset;
        }
    }
}
