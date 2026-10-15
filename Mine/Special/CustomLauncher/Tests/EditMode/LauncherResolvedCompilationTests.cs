using System;
using System.Collections.Generic;
using System.Linq;
using CelToon.CustomLauncher.Compilation;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Definitions;
using CelToon.CustomLauncher.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CelToon.CustomLauncher.Tests.EditMode
{
    public sealed class LauncherResolvedCompilationTests
    {
        private readonly List<UnityEngine.Object> garbage = new List<UnityEngine.Object>();
        private T Create<T>() where T : ScriptableObject
        { var value = ScriptableObject.CreateInstance<T>(); garbage.Add(value); return value; }
        [TearDown] public void Cleanup()
        { foreach (var value in garbage) if (value != null) UnityEngine.Object.DestroyImmediate(value); garbage.Clear(); }

        private static void EqualPlans(CompiledLauncherPlan a, CompiledLauncherPlan b)
        {
            Assert.AreEqual(a.RecipeId, b.RecipeId); Assert.AreEqual(a.ProjectileId, b.ProjectileId);
            Assert.AreEqual(a.Projectile, b.Projectile); Assert.AreEqual(a.Distribution, b.Distribution);
            Assert.AreEqual(a.DistributionCount, b.DistributionCount); Assert.AreEqual(a.SpreadDegrees, b.SpreadDegrees);
            Assert.AreEqual(a.BurstCount, b.BurstCount); Assert.AreEqual(a.BurstInterval, b.BurstInterval);
            Assert.AreEqual(a.EnergyCost, b.EnergyCost); Assert.AreEqual(a.Cooldown, b.Cooldown);
            Assert.AreEqual(a.Program.MaxGenerated, b.Program.MaxGenerated);
            CollectionAssert.AreEqual(a.Program.Steps, b.Program.Steps);
        }

        [Test] public void ShippedRecipesCompileIdenticallyThroughDetachedValues()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PartCatalog>("Assets/Mine/Special/CustomLauncher/Samples/Definitions/Catalog.asset");
            Assert.IsNotNull(catalog);
            foreach (var recipe in catalog.Recipes)
            {
                var loadout = recipe.CreateLoadout();
                var legacy = LauncherCompiler.Compile(loadout);
                var resolved = LauncherCompiler.Compile(recipe, loadout.Parts.Select(LauncherResolvedPart.FromInstance).ToArray());
                Assert.IsTrue(legacy.Success, recipe.DisplayName); Assert.IsTrue(resolved.CanCommit, recipe.DisplayName);
                EqualPlans(legacy.Plan, resolved.Plan);
            }
        }

        [Test] public void DistributionOverridesAreSavedValuesAndNullEntriesRemainEmpty()
        {
            var recipe = Create<LauncherRecipeDefinition>(); var projectile = Create<ProjectileDefinition>(); projectile.PartId = "round";
            var distribution = Create<DistributionDefinition>(); distribution.PartId = "fan"; distribution.Mode = DistributionMode.Fan;
            var loadout = new LauncherLoadout(recipe, new PartInstance("projectile.main", projectile),
                new PartInstance("distribution.main", distribution) { OverrideDistribution = true, Count = 7, SpreadDegrees = 37 });
            var values = loadout.Parts.Select(LauncherResolvedPart.FromInstance).ToArray();
            var saved = LauncherCompiler.Compile(recipe, values);
            EqualPlans(LauncherCompiler.Compile(loadout).Plan, saved.Plan);
            distribution.Count = 2; distribution.SpreadDegrees = 8; projectile.Damage = 90;
            var after = LauncherCompiler.Compile(recipe, values); EqualPlans(saved.Plan, after.Plan);
            Assert.IsTrue(LauncherCompiler.Compile(recipe, new LauncherResolvedPart[] { null, null }).Success);
            Assert.IsFalse(LauncherCompiler.Compile(recipe, (IReadOnlyList<LauncherResolvedPart>)null).Success);
        }

        [Test] public void OperationCollectionsAreCopiedAndCannotBeModified()
        {
            var steps = new[] { new EffectStep(EffectOperation.DamageScale, 1.5f) };
            var part = new LauncherResolvedPart("modifier.main", "boost", "Boost", PartKind.Modifier, operations: steps);
            steps[0].A = 9;
            Assert.AreEqual(1.5f, part.Operations[0].A);
            Assert.Throws<NotSupportedException>(() => ((IList<EffectStep>)part.Operations)[0] = steps[0]);
        }

        [Test] public void TwoValuesForOneTemplateUseIndependentProjectileParameters()
        {
            var recipe = Create<LauncherRecipeDefinition>(); var template = Create<ProjectileDefinition>(); template.PartId = "round";
            var first = LauncherResolvedPart.FromDefinition("projectile.main", template);
            var second = new LauncherResolvedPart(first.SlotId, first.PartId, first.DisplayName, first.Kind,
                new ProjectileSpec(25, 40, .1f, 4, Color.green), energyPerProjectile: 3);
            var a = LauncherCompiler.Compile(recipe, new[] { first }); var b = LauncherCompiler.Compile(recipe, new[] { second });
            Assert.AreEqual(10, a.Plan.Projectile.Damage); Assert.AreEqual(25, b.Plan.Projectile.Damage);
            Assert.AreEqual(10, template.Damage); Assert.AreEqual(4, b.Plan.EnergyCost);
        }

        private sealed class Emitter : IProjectileEmitter
        { public bool TryEmit(IReadOnlyList<ProjectileCommand> commands) => true; }

        [Test] public void CompiledCommitPreservesEnergyCooldownCountersAndFailedCommitIsAtomic()
        {
            var go = new GameObject("ResolvedHost"); garbage.Add(go); var host = go.AddComponent<LauncherHost>();
            var recipe = Create<LauncherRecipeDefinition>(); var projectile = Create<ProjectileDefinition>(); projectile.PartId = "round";
            var draft = new LauncherLoadout(recipe, new PartInstance("projectile.main", projectile));
            Assert.IsTrue(host.TryEquip(draft));
            Assert.AreEqual(FireResult.Fired, host.TryFire(new Emitter(), new FireAim(Vector3.zero, Vector3.forward, Vector3.up), 10));
            var state = host.State; float energy = state.Energy, next = state.NextFireTime; int shots = state.ShotCount;
            var valid = LauncherCompiler.Compile(recipe, new[] { LauncherResolvedPart.FromDefinition("projectile.main", projectile) });
            Assert.IsTrue(host.TryCommitCompiled(valid)); Assert.AreSame(state, host.State); Assert.IsNull(host.Loadout);
            Assert.AreEqual(energy, state.Energy); Assert.AreEqual(next, state.NextFireTime); Assert.AreEqual(shots, state.ShotCount);
            var before = state.Plan;
            var invalid = LauncherCompiler.Compile(recipe, new[] { new LauncherResolvedPart("projectile.main", "round", "Invalid", PartKind.Projectile) });
            Assert.IsFalse(host.TryCommitCompiled(invalid)); Assert.IsFalse(host.TryCommitCompiled(null));
            Assert.AreSame(before, state.Plan); Assert.AreEqual(energy, state.Energy); Assert.AreEqual(next, state.NextFireTime);
        }

        [Test] public void LegacyLauncherHostSnapshotsBothIncomingAndReturnedSlotData()
        {
            var go = new GameObject("SnapshotHost"); garbage.Add(go); var host = go.AddComponent<LauncherHost>();
            var recipe = Create<LauncherRecipeDefinition>(); var distribution = Create<DistributionDefinition>();
            distribution.PartId = "fan"; distribution.Mode = DistributionMode.Fan;
            var draft = new LauncherLoadout(recipe, new PartInstance("distribution.main", distribution) { OverrideDistribution = true, Count = 3, SpreadDegrees = 20 });
            Assert.IsTrue(host.TryEquip(draft)); draft.Parts[0].Count = 50; draft.Parts[0].SlotId = "invalid";
            var saved = host.Loadout; Assert.AreEqual(3, saved.Parts[0].Count); Assert.AreEqual("distribution.main", saved.Parts[0].SlotId);
            saved.Parts[0].Count = 40; saved.Parts[0].SpreadDegrees = 90;
            Assert.AreEqual(3, host.Loadout.Parts[0].Count); Assert.AreEqual(20, host.Loadout.Parts[0].SpreadDegrees);
        }
    }
}
