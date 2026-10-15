using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CelToon.CustomEquipment.Compilation;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Definitions;
using UnityEditor;
using UnityEngine;

namespace CelToon.CustomEquipment.Editor
{
    // Preserve legacy values and operation order; generated cores are stable assets, not transient SOs.
    public static class EquipmentCoreMigration
    {
        private const string Folder = StorageSceneBuilder.RootPath + "/Samples/Definitions/MigratedCores";
        [Serializable] private sealed class Steps { public EquipmentStep[] Operations; }
        public static bool UpgradeRecipe(EquipmentRecipeDefinition recipe)
        {
            if (recipe == null || recipe.Parts == null) return false;
            bool legacy = recipe.Parts.Any(p => p != null && EquipmentSlots.Legacy(p.Kind));
            bool schema = recipe.ResourceSchemaVersion == 0;
            if (!legacy && !schema) return false;
            var upgraded = legacy ? Combine(recipe.Parts, Folder) : recipe.Parts;
            var stats = recipe.BaseStats;
            // Missing serialized energy fields in old assets are all zero. Preserve authored new values.
            if (schema && stats.MaxEnergy == 0 && stats.EnergyRegen == 0 && stats.EnergyCostScale == 0) {
                stats.MaxEnergy = 100; stats.EnergyRegen = 10; stats.EnergyCostScale = 1;
            }
            var draft = new EquipmentLoadout(recipe, upgraded.Select(p => new EquipmentPartInstance(EquipmentSlots.Id(p.Kind), p)).ToArray());
            // Compile against the new recipe before changing the real asset.
            var probe = ScriptableObject.CreateInstance<EquipmentRecipeDefinition>();
            try {
                probe.RecipeId = recipe.RecipeId; probe.BaseStats = stats; probe.Parts = upgraded;
                draft.Recipe = probe;
                var result = EquipmentCompiler.Compile(draft);
                if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics));
            } finally { UnityEngine.Object.DestroyImmediate(probe); }
            recipe.Parts = upgraded; recipe.BaseStats = stats; recipe.ResourceSchemaVersion = 1;
            if (legacy) recipe.Description = (recipe.Description ?? "").Replace("八个槽位", "四个资源槽位")
                + "\n资源核心兼容配方：旧效果归入主属性/固定副词条；替换核心会替换整组加成。";
            EditorUtility.SetDirty(recipe); AssetDatabase.SaveAssetIfDirty(recipe); return true;
        }
        public static EquipmentLoadout UpgradeLoadout(EquipmentLoadout source, string outputFolder = null)
        {
            if (source?.Recipe == null || source.Parts == null) return source;
            bool legacy = source.Parts.Any(p => p != null && IsLegacySlot(p.SlotId));
            if (!legacy) return source;
            var slots = new HashSet<string>(); var definitions = new List<EquipmentPartDefinition>();
            foreach (var instance in source.Parts) {
                if (instance == null || !IsLegacySlot(instance.SlotId) || !slots.Add(instance.SlotId))
                    throw new InvalidOperationException("旧装配含未知、重复或混合槽位，未自动修改。");
                if (instance.Part == null) continue;
                if (!EquipmentSlots.Legacy(instance.Part.Kind) || EquipmentSlots.Id(instance.Part.Kind) != instance.SlotId)
                    throw new InvalidOperationException("旧配件与槽位不匹配，未自动修改。");
                definitions.Add(instance.Part);
            }
            var result = new EquipmentLoadout(source.Recipe);
            result.Parts = new EquipmentPartInstance[EquipmentSlots.Count];
            for (int i = 0; i < result.Parts.Length; i++) result.Parts[i] = new EquipmentPartInstance(EquipmentSlots.Id(EquipmentSlots.At(i)), null);
            foreach (var part in Combine(definitions.ToArray(), outputFolder ?? Folder)) result.Find(part.Kind).Part = part;
            return result;
        }
        private static bool IsLegacySlot(string slot)
        { for (int i = 0; i < 12; i++) if (slot == EquipmentSlots.Id((EquipmentKind)i)) return true; return false; }
        private static EquipmentPartDefinition[] Combine(EquipmentPartDefinition[] definitions, string folder)
        {
            var groups = new List<EquipmentStep>[EquipmentSlots.Count]; var names = new List<string>[EquipmentSlots.Count];
            for (int i = 0; i < groups.Length; i++) { groups[i] = new List<EquipmentStep>(); names[i] = new List<string>(); }
            var kinds = new HashSet<EquipmentKind>();
            foreach (var part in definitions.OrderBy(p => p == null ? -1 : (int)p.Kind)) {
                if (part == null || !kinds.Add(part.Kind) || part.Operations == null)
                    throw new InvalidOperationException("旧配方含空、重复或无效配件，未自动修改。");
                bool old = EquipmentSlots.Legacy(part.Kind);
                if (!old && (!EquipmentSlots.Supported(part.Kind) || part.PrimaryOperations == null || part.Affixes == null))
                    throw new InvalidOperationException("未知配件类别。");
                var operations = old ? part.Operations : part.PrimaryOperations.Concat(part.Affixes).ToArray();
                if (operations.Length == 0 || operations.Length > (EquipmentSlots.EightSlot(part.Kind) ? 8 : old ? 24 : EquipmentSlots.MaxOperationsPerPart))
                    throw new InvalidOperationException("旧配件操作数无效。");
                foreach (var step in operations) {
                    if (!step.IsValid || (old ? !LegacyAccepts(part.Kind, step) : !EquipmentSlots.Accepts(part.Kind, step)))
                        throw new InvalidOperationException("旧配件操作无效：" + part.DisplayName);
                    var destination = old ? step.CoreKind : part.Kind;
                    int index = EquipmentSlots.Index(destination);
                    if (index < 0) throw new InvalidOperationException("未知配件类别。");
                    groups[index].Add(step);
                    if (!names[index].Contains(part.DisplayName)) names[index].Add(part.DisplayName);
                }
            }
            var result = new List<EquipmentPartDefinition>();
            for (int i = 0; i < groups.Length; i++) {
                if (groups[i].Count == 0) continue;
                if (groups[i].Count > EquipmentSlots.MaxOperationsPerPart) throw new InvalidOperationException("兼容核心操作超出预算。");
                var kind = EquipmentSlots.At(i);
                var primary = groups[i].Where(step => EquipmentSlots.IsPrimary(step.Operation)).ToArray();
                var affixes = groups[i].Where(step => !EquipmentSlots.IsPrimary(step.Operation)).ToArray();
                // Legacy utility-only groups get a neutral main stat, retaining every old effect.
                if (primary.Length == 0) primary = new[] { new EquipmentStep(EquipmentSlots.PrimaryOperation(kind), 0) };
                var operations = primary.Concat(affixes).ToArray();
                if (operations.Length > EquipmentSlots.MaxOperationsPerPart) throw new InvalidOperationException("兼容核心操作超出预算。");
                string hash;
                using (var algorithm = SHA256.Create()) {
                    var bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(kind + JsonUtility.ToJson(new Steps { Operations = operations })));
                    hash = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                }
                StorageSceneBuilder.EnsureFolder(folder);
                string path = folder + "/Resource_" + kind + "_" + hash + ".asset";
                var core = AssetDatabase.LoadAssetAtPath<EquipmentPartDefinition>(path);
                if (core == null) {
                    core = ScriptableObject.CreateInstance<EquipmentPartDefinition>();
                    core.PartId = "equipment.compat." + kind.ToString().ToLowerInvariant() + "." + hash;
                    core.DisplayName = "兼容·" + EquipmentSlots.Name(kind); core.Kind = kind;
                    core.Description = "原装配归并：" + string.Join(" + ", names[i]) + "。效果参数保留；替换本核心会替换整组加成。";
                    core.PrimaryOperations = primary; core.Affixes = affixes; AssetDatabase.CreateAsset(core, path);
                }
                result.Add(core);
            }
            return result.ToArray();
        }
        private static bool LegacyAccepts(EquipmentKind kind, EquipmentStep step)
        {
            if (EquipmentSlots.EightSlot(kind)) return step.Kind == kind;
            int op = (int)step.Operation;
            if (op > 22) return false;
            int previous = op <= 3 || (op >= 7 && op <= 11) ? 8 : op <= 6 ? 9
                : op <= 13 || op == 17 || op == 18 || op == 22 ? 11 : 10;
            return (int)kind == previous || (kind == EquipmentKind.MobilityDrive
                && (step.Operation == EquipmentOperation.SlowResistance || step.Operation == EquipmentOperation.StunResistance));
        }
    }
}
