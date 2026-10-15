using System;
using System.Collections.Generic;
using System.Linq;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Definitions;

namespace CelToon.CustomLauncher.Compilation
{
    public sealed class CompileResult
    {
        public CompiledLauncherPlan Plan { get; }
        public IReadOnlyList<CompileDiagnostic> Diagnostics { get; }
        public bool Success => Plan != null && Diagnostics.Count == 0;
        public bool CanCommit => Success;
        internal CompileResult(CompiledLauncherPlan plan, List<CompileDiagnostic> diagnostics)
        { Plan = plan; Diagnostics = Array.AsReadOnly(diagnostics.ToArray()); }
    }
    public static class LauncherCompiler
    {
        private static readonly PartKind[] order = { PartKind.Projectile, PartKind.Modifier, PartKind.Distribution,
            PartKind.Trajectory, PartKind.Damage, PartKind.Impact, PartKind.Trigger, PartKind.FireControl };
        public static CompileResult Compile(LauncherLoadout loadout)
        {
            var errors = new List<CompileDiagnostic>();
            if (loadout?.Parts != null) foreach (var instance in loadout.Parts)
            {
                var part = instance?.Part;
                if (part == null) continue;
                if (instance.OverrideDistribution && !(part is DistributionDefinition))
                    errors.Add(new CompileDiagnostic("part.override", instance.SlotId, part.PartId, "Only distribution parts accept distribution overrides."));
                if (!(part is ProjectileDefinition) && !(part is DistributionDefinition) && !(part is EffectPartDefinition))
                    errors.Add(new CompileDiagnostic("part.unsupported", instance.SlotId, part.PartId, "Unsupported part definition."));
                // Preserve serialization checks before the detached-value compiler takes over.
                if (part is EffectPartDefinition && (int)part.Kind < 2)
                    errors.Add(new CompileDiagnostic("effect.definition", instance.SlotId, part.PartId, "An effect part needs 1..8 valid operations and a finite cost."));
            }
            return CompileCore(loadout?.Recipe, loadout?.Parts?.Select(LauncherResolvedPart.FromInstance).ToArray(), errors);
        }

        /// <summary>Compile saved values through the same slot, operation and budget checks as legacy loadouts.</summary>
        public static CompileResult Compile(LauncherRecipeDefinition recipe, IReadOnlyList<LauncherResolvedPart> parts)
            => CompileCore(recipe, parts, new List<CompileDiagnostic>());

        private static CompileResult CompileCore(LauncherRecipeDefinition recipe, IReadOnlyList<LauncherResolvedPart> parts,
            List<CompileDiagnostic> errors)
        {
            Action<string, string, string, string> error = (c, s, p, m) => errors.Add(new CompileDiagnostic(c, s, p, m));
            if (recipe == null)
            { error("recipe.missing", "", "", "Choose a recipe, including Custom / Empty."); return new CompileResult(null, errors); }
            if (string.IsNullOrWhiteSpace(recipe.RecipeId) || !LauncherLimits.InRange(recipe.Cooldown, .02f, 60f)
                || !LauncherLimits.InRange(recipe.BaseEnergyCost, 0f, 1000000f) || recipe.MaxProjectilesPerShot < 1
                || recipe.MaxProjectilesPerShot > 128 || recipe.MaxGeneratedPerShot < 1 || recipe.MaxGeneratedPerShot > 4096)
                error("recipe.parameters", "", "", "Check recipe ID, cooldown, costs and projectile limits.");
            var recipeKinds = new HashSet<PartKind>();
            if (recipe.Parts == null || recipe.Parts.Length > 8) error("recipe.parts", "", "", "A recipe has at most one part per category.");
            else foreach (var part in recipe.Parts)
                if (part != null && (!LauncherSlots.Supported(part.Kind) || !recipeKinds.Add(part.Kind)))
                    error("recipe.duplicate_kind", "", part.PartId, "Use a composite part instead of two parts of the same category.");
            var equipped = new LauncherResolvedPart[8]; var ids = new HashSet<string>();
            if (parts == null || parts.Count > 8) error("loadout.parts", "", "", "Provide at most eight slot entries.");
            else foreach (var part in parts)
            {
                if (part == null) continue; // A missing resolved value leaves its slot empty.
                int index = -1;
                for (int i = 0; i < 8; i++) if (part.SlotId == LauncherSlots.Id((PartKind)i)) index = i;
                if (index < 0) { error("part.unknown_slot", part.SlotId, part.PartId, "Choose one of the eight fixed slots."); continue; }
                if (equipped[index] != null) { error("part.duplicate_slot", part.SlotId, part.PartId, "One slot accepts one part, including composite parts."); continue; }
                equipped[index] = part;
                if (part.IsEmpty) continue;
                if (part.Kind != (PartKind)index) error("part.type", part.SlotId, part.PartId, "Choose a part matching the slot category.");
                if (string.IsNullOrWhiteSpace(part.PartId)) error("part.id", part.SlotId, "", "Set a non-empty part ID.");
                else if (!ids.Add(part.PartId)) error("part.duplicate_id", part.SlotId, part.PartId, "Different definitions must have unique IDs.");
                if ((part.Kind == PartKind.Projectile || part.Kind == PartKind.Distribution) && part.Operations != null && part.Operations.Count != 0)
                    error("part.operations", part.SlotId, part.PartId, "Projectile and distribution values use their own parameters, not effect operations.");
            }
            var projectile = Selected(equipped[0], PartKind.Projectile);
            var spec = projectile != null ? projectile.Projectile : default;
            float perProjectile = projectile != null ? projectile.EnergyPerProjectile : 0f;
            if (projectile != null && (!spec.IsValid || !LauncherLimits.InRange(perProjectile, 0f, 1000000f)))
                error("projectile.parameters", LauncherSlots.Id(PartKind.Projectile), projectile.PartId, "Check finite projectile parameters.");
            int count = 1, burst = 1; float spread = 0f, interval = .1f, cooldown = recipe.Cooldown, costScale = 1f;
            var mode = DistributionMode.Single;
            var distribution = Selected(equipped[1], PartKind.Distribution);
            if (distribution != null)
            {
                mode = distribution.DistributionMode; count = distribution.Count; spread = distribution.SpreadDegrees;
                if (!Enum.IsDefined(typeof(DistributionMode), mode) || count < 1 || count > 128
                    || !LauncherLimits.InRange(spread, 0f, 180f) || (mode == DistributionMode.Single && count != 1)
                    || distribution.EnergyPerProjectile != 0)
                    error("distribution.parameters", distribution.SlotId, distribution.PartId, "Check mode, count and spread (0..180 degrees); distribution carries no separate cost.");
            }
            var steps = new List<EffectStep>(); int descendants = 1;
            foreach (var kind in order)
            {
                if ((int)kind < 2) continue;
                var effect = Selected(equipped[(int)kind], kind); if (effect == null) continue;
                if (effect.Operations == null || effect.Operations.Count < 1 || effect.Operations.Count > 8
                    || !LauncherLimits.InRange(effect.EnergyPerProjectile, 0f, 1000000f))
                { error("effect.definition", LauncherSlots.Id(kind), effect.PartId, "An effect part needs 1..8 valid operations and a finite cost."); continue; }
                perProjectile += effect.EnergyPerProjectile;
                foreach (var step in effect.Operations)
                {
                    if (!step.IsValid || step.Kind != kind)
                    { error("effect.operation", LauncherSlots.Id(kind), effect.PartId, "Check operation parameters and category."); continue; }
                    switch (step.Operation)
                    {
                        case EffectOperation.DamageScale: spec = new ProjectileSpec(spec.Damage * step.A, spec.Speed, spec.Radius, spec.Lifetime, spec.Color); break;
                        case EffectOperation.SpeedScale: spec = new ProjectileSpec(spec.Damage, spec.Speed * step.A, spec.Radius, spec.Lifetime, spec.Color); break;
                        case EffectOperation.RadiusScale: spec = new ProjectileSpec(spec.Damage, spec.Speed, spec.Radius * step.A, spec.Lifetime, spec.Color); break;
                        case EffectOperation.LifetimeScale: spec = new ProjectileSpec(spec.Damage, spec.Speed, spec.Radius, spec.Lifetime * step.A, spec.Color); break;
                        case EffectOperation.CostScale: costScale *= step.A; break;
                        case EffectOperation.CooldownScale: cooldown *= step.A; break;
                        case EffectOperation.Burst: burst *= step.Count; interval = step.Delay; break;
                        case EffectOperation.TimedSplit: case EffectOperation.HitSplit: case EffectOperation.ExpirySplit: descendants += step.Count; break;
                    }
                    steps.Add(step);
                }
            }
            cooldown = Math.Max(cooldown, (burst - 1) * interval + .02f);
            float energy = projectile != null ? (recipe.BaseEnergyCost + perProjectile * count * burst * descendants) * costScale : 0f;
            if (count * (long)burst > recipe.MaxProjectilesPerShot || count * (long)burst * descendants > recipe.MaxGeneratedPerShot
                || burst > 8 || steps.Count > 32) error("budget.generated", "", "", "Reduce distribution, burst or split counts to fit the recipe budget.");
            if (!LauncherLimits.InRange(energy, 0f, 1000000f) || !LauncherLimits.InRange(cooldown, .02f, 60f)
                || (projectile != null && !spec.IsValid)) error("result.parameters", "", "", "Combined parameters exceed supported ranges.");
            if (errors.Count != 0) return new CompileResult(null, errors);
            var program = new ProjectileProgram(steps.ToArray(), recipe.MaxGeneratedPerShot);
            return new CompileResult(new CompiledLauncherPlan(recipe.RecipeId, projectile != null ? projectile.PartId : "", spec,
                mode, count, spread, cooldown, energy, program, burst, interval), errors);
        }
        private static LauncherResolvedPart Selected(LauncherResolvedPart part, PartKind kind)
            => part != null && !part.IsEmpty && part.Kind == kind ? part : null;
    }
}
