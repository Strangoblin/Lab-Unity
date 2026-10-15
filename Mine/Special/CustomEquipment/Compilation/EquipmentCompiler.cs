using System;
using System.Collections.Generic;
using System.Linq;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Definitions;

namespace CelToon.CustomEquipment.Compilation
{
    public sealed class EquipmentCompileResult
    {
        public CompiledEquipmentPlan Plan { get; }
        public IReadOnlyList<string> Diagnostics { get; }
        public bool Success => Plan != null && Diagnostics.Count == 0;
        private readonly bool compilerValidated;
        public bool CanCommit => Success && compilerValidated;
        public EquipmentCompileResult(CompiledEquipmentPlan plan, List<string> diagnostics)
            : this(plan, diagnostics, false) { }
        internal EquipmentCompileResult(CompiledEquipmentPlan plan, List<string> diagnostics, bool compilerValidated)
        { Plan = plan; Diagnostics = Array.AsReadOnly(diagnostics.ToArray()); this.compilerValidated = compilerValidated; }
    }
    public static class EquipmentCompiler
    {
        public static EquipmentCompileResult Compile(EquipmentLoadout loadout)
        {
            var errors = new List<string>();
            if (loadout?.Parts != null) foreach (var instance in loadout.Parts)
                if (instance?.Part != null && (instance.Part.Operations == null || instance.Part.Operations.Length != 0))
                    errors.Add("part.operations：核心需要主属性；总操作超限或含未迁移旧数据");
            return CompileCore(loadout?.Recipe, loadout?.Parts?.Select(EquipmentResolvedPart.FromInstance).ToArray(), errors);
        }

        /// <summary>Compile detached per-item values using the same resource and operation rules as asset loadouts.</summary>
        public static EquipmentCompileResult Compile(EquipmentRecipeDefinition recipe, IReadOnlyList<EquipmentResolvedPart> parts)
            => CompileCore(recipe, parts, new List<string>());

        private static EquipmentCompileResult CompileCore(EquipmentRecipeDefinition recipe,
            IReadOnlyList<EquipmentResolvedPart> parts, List<string> errors)
        {
            if (recipe == null) { errors.Add("recipe.missing：请先选择装备配方"); return new EquipmentCompileResult(null, errors, true); }
            if (string.IsNullOrWhiteSpace(recipe.RecipeId) || !recipe.BaseStats.IsValid) errors.Add("recipe.parameters：配方基础属性无效");
            var recipeKinds = new HashSet<EquipmentKind>();
            if (recipe.Parts == null) errors.Add("recipe.parts：初始配件数组为空");
            else foreach (var part in recipe.Parts)
                if (part == null || !EquipmentSlots.Supported(part.Kind) || !recipeKinds.Add(part.Kind)) errors.Add("recipe.duplicate：配方每类最多一个配件");
            var equipped = new EquipmentResolvedPart[EquipmentSlots.Count];
            var slots = new HashSet<string>(); var ids = new HashSet<string>();
            if (parts == null || parts.Count > EquipmentSlots.Count) errors.Add("slots.count：最多四个核心槽位");
            else foreach (var part in parts)
            {
                if (part == null) continue;
                if (string.IsNullOrWhiteSpace(part.SlotId) || !slots.Add(part.SlotId))
                { errors.Add("slots.duplicate：槽位缺失或重复"); continue; }
                int index = -1;
                for (int i = 0; i < EquipmentSlots.Count; i++) if (part.SlotId == EquipmentSlots.Id(EquipmentSlots.At(i))) index = i;
                if (index < 0) { errors.Add("slots.unknown：未知槽位 " + part.SlotId); continue; }
                if (part.IsEmpty) continue;
                if (EquipmentSlots.Index(part.Kind) != index) { errors.Add("part.kind：配件类别与槽位不匹配"); continue; }
                if (string.IsNullOrWhiteSpace(part.PartId) || !ids.Add(part.PartId)) errors.Add("part.id：配件 ID 为空或重复");
                equipped[index] = part;
            }
            var steps = new List<EquipmentStep>(); var stats = recipe.BaseStats; float healthScale = 1;
            for (int i = 0; i < equipped.Length; i++)
            {
                var part = equipped[i]; if (part == null) continue;
                if (part.PrimaryOperations == null || part.Affixes == null || part.PrimaryOperations.Count < 1
                    || part.PrimaryOperations.Count + part.Affixes.Count > EquipmentSlots.MaxOperationsPerPart)
                { errors.Add("part.operations：核心需要主属性；总操作超限或含未迁移旧数据"); continue; }
                if (part.PrimaryOperations.Any(step => !EquipmentSlots.AcceptsPrimary(part.Kind, step))
                    || part.Affixes.Any(step => !EquipmentSlots.AcceptsAffix(step)))
                { errors.Add("part.primary：主属性必须对应本槽资源，其他效果放入副词条"); continue; }
                foreach (var step in part.PrimaryOperations.Concat(part.Affixes))
                {
                    if (!EquipmentSlots.Accepts(part.Kind, step)) { errors.Add("part.operation：" + part.DisplayName + " 的参数或操作类别无效"); continue; }
                    steps.Add(step);
                    switch (step.Operation)
                    {
                        case EquipmentOperation.HealthAdd: stats.MaxHealth += step.A; break;
                        case EquipmentOperation.HealthScale: healthScale *= step.A; break;
                        case EquipmentOperation.ArmorAdd: stats.Armor += step.A; break;
                        case EquipmentOperation.DamageScale: stats.DamageScale *= step.A; break;
                        case EquipmentOperation.ShieldAdd: stats.MaxShield += step.A; break;
                        case EquipmentOperation.ShieldRegen: stats.ShieldRegen += step.A; break;
                        case EquipmentOperation.ShieldDelay: stats.ShieldDelay = Math.Min(stats.ShieldDelay, step.A); break;
                        case EquipmentOperation.BurnResistance: stats.BurnResistance = Combine(stats.BurnResistance, step.A); break;
                        case EquipmentOperation.PoisonResistance: stats.PoisonResistance = Combine(stats.PoisonResistance, step.A); break;
                        case EquipmentOperation.SlowResistance: stats.SlowResistance = Combine(stats.SlowResistance, step.A); break;
                        case EquipmentOperation.StunResistance: stats.StunResistance = Combine(stats.StunResistance, step.A); break;
                        case EquipmentOperation.MarkResistance: stats.MarkResistance = Combine(stats.MarkResistance, step.A); break;
                        case EquipmentOperation.HealthRegen: stats.HealthRegen += step.A; break;
                        case EquipmentOperation.RecoveryDelay: stats.RecoveryDelay = Math.Min(stats.RecoveryDelay, step.A); break;
                        case EquipmentOperation.MoveScale: stats.MoveSpeed *= step.A; break;
                        case EquipmentOperation.SprintScale: stats.SprintMultiplier *= step.A; break;
                        case EquipmentOperation.ImpulseResistance: stats.ImpulseResistance = Combine(stats.ImpulseResistance, step.A); break;
                        case EquipmentOperation.HitInvulnerability: stats.HitInvulnerability = Math.Max(stats.HitInvulnerability, step.A); break;
                        case EquipmentOperation.EmergencyHeal: stats.EmergencyHealFraction = Math.Max(stats.EmergencyHealFraction, step.A); stats.EmergencyCooldown = Math.Max(stats.EmergencyCooldown, step.B); break;
                        case EquipmentOperation.StaminaAdd: stats.MaxStamina += step.A; break;
                        case EquipmentOperation.StaminaRegen: stats.StaminaRegen += step.A; break;
                        case EquipmentOperation.SprintCostScale: stats.SprintCost *= step.A; break;
                        case EquipmentOperation.CleanseInterval: stats.CleanseInterval = stats.CleanseInterval == 0 ? step.A : Math.Min(stats.CleanseInterval, step.A); break;
                        case EquipmentOperation.EnergyAdd: stats.MaxEnergy += step.A; break;
                        case EquipmentOperation.EnergyRegen: stats.EnergyRegen += step.A; break;
                        case EquipmentOperation.EnergyCostScale: stats.EnergyCostScale *= step.A; break;
                    }
                }
            }
            stats.MaxHealth *= healthScale;
            if (!stats.IsValid || steps.Count > EquipmentSlots.MaxOperationsPerLoadout) errors.Add("result.parameters：合成属性超出安全范围");
            return new EquipmentCompileResult(errors.Count == 0 ? new CompiledEquipmentPlan(recipe.RecipeId, stats, steps.ToArray()) : null, errors, true);
        }
        private static float Combine(float a, float b) => Math.Min(.95f, 1 - (1 - a) * (1 - b));
    }
}
