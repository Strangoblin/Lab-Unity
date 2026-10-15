using System;
using System.Collections.Generic;
using System.Linq;
using CelToon.CustomEquipment.Compilation;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Definitions;
using CelToon.CustomEquipment.Runtime;
using CelToon.CustomLauncher.Contracts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CelToon.CustomEquipment.Tests.EditMode
{
    public sealed class EquipmentResolvedCompilationTests
    {
        private readonly List<UnityEngine.Object> garbage = new List<UnityEngine.Object>();
        private T Create<T>() where T : ScriptableObject
        { var value = ScriptableObject.CreateInstance<T>(); garbage.Add(value); return value; }
        [TearDown] public void Cleanup()
        { foreach (var value in garbage) if (value != null) UnityEngine.Object.DestroyImmediate(value); garbage.Clear(); }

        [Test] public void ShippedRecipesCompileIdenticallyThroughDetachedValues()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<EquipmentCatalog>("Assets/Mine/Special/CustomEquipment/Samples/Definitions/Catalog.asset");
            Assert.IsNotNull(catalog);
            foreach (var recipe in catalog.Recipes)
            {
                var loadout = recipe.CreateLoadout();
                var legacy = EquipmentCompiler.Compile(loadout);
                var resolved = EquipmentCompiler.Compile(recipe, loadout.Parts.Select(EquipmentResolvedPart.FromInstance).ToArray());
                Assert.IsTrue(legacy.Success, recipe.DisplayName); Assert.IsTrue(resolved.CanCommit, recipe.DisplayName);
                Assert.AreEqual(legacy.Plan.RecipeId, resolved.Plan.RecipeId); Assert.AreEqual(legacy.Plan.Stats, resolved.Plan.Stats);
                CollectionAssert.AreEqual(legacy.Plan.Steps, resolved.Plan.Steps);
            }
        }

        [Test] public void ValuesCopyBothOperationGroupsAndNeverReadTemplateAgain()
        {
            var template = Create<EquipmentPartDefinition>(); template.PartId = "health"; template.Kind = EquipmentKind.HealthCore;
            template.PrimaryOperations = new[] { new EquipmentStep(EquipmentOperation.HealthAdd, 40) };
            template.Affixes = new[] { new EquipmentStep(EquipmentOperation.ArmorAdd, 3) };
            var values = EquipmentResolvedPart.FromDefinition("healthcore.main", template);
            template.PrimaryOperations[0].A = 400; template.Affixes[0].A = 30;
            var recipe = Create<EquipmentRecipeDefinition>(); var result = EquipmentCompiler.Compile(recipe, new[] { values });
            Assert.IsTrue(result.Success); Assert.AreEqual(140, result.Plan.Stats.MaxHealth); Assert.AreEqual(3, result.Plan.Stats.Armor);
            Assert.Throws<NotSupportedException>(() => ((IList<EquipmentStep>)values.Affixes)[0] = new EquipmentStep(EquipmentOperation.ArmorAdd, 9));
            Assert.Throws<NotSupportedException>(() => ((IList<EquipmentStep>)values.PrimaryOperations)[0] = new EquipmentStep(EquipmentOperation.HealthAdd, 9));
        }

        [Test] public void SameTemplateSupportsIndependentInstanceParametersWithoutNewAssets()
        {
            var recipe = Create<EquipmentRecipeDefinition>();
            var first = new EquipmentResolvedPart("energycore.main", "cell", "Cell", EquipmentKind.EnergyCore,
                new[] { new EquipmentStep(EquipmentOperation.EnergyAdd, 20) }, Array.Empty<EquipmentStep>());
            var second = new EquipmentResolvedPart(first.SlotId, first.PartId, first.DisplayName, first.Kind,
                new[] { new EquipmentStep(EquipmentOperation.EnergyAdd, 60) }, new[] { new EquipmentStep(EquipmentOperation.EnergyRegen, 4) });
            var a = EquipmentCompiler.Compile(recipe, new[] { first }); var b = EquipmentCompiler.Compile(recipe, new[] { second });
            Assert.AreEqual(recipe.BaseStats.MaxEnergy + 20, a.Plan.Stats.MaxEnergy);
            Assert.AreEqual(recipe.BaseStats.MaxEnergy + 60, b.Plan.Stats.MaxEnergy);
            Assert.AreEqual(recipe.BaseStats.EnergyRegen + 4, b.Plan.Stats.EnergyRegen);
        }

        [Test] public void NullResolvedEntriesAreEmptyButWrongPrimaryAndResourceAffixesAreRejected()
        {
            var recipe = Create<EquipmentRecipeDefinition>();
            Assert.IsTrue(EquipmentCompiler.Compile(recipe, new EquipmentResolvedPart[] { null, null }).Success);
            Assert.IsFalse(EquipmentCompiler.Compile(recipe, (IReadOnlyList<EquipmentResolvedPart>)null).Success);
            var wrong = new EquipmentResolvedPart("healthcore.main", "bad", "Bad", EquipmentKind.HealthCore,
                new[] { new EquipmentStep(EquipmentOperation.ShieldAdd, 10) }, Array.Empty<EquipmentStep>());
            Assert.IsFalse(EquipmentCompiler.Compile(recipe, new[] { wrong }).Success);
            var hiddenCap = new EquipmentResolvedPart("healthcore.main", "bad", "Bad", EquipmentKind.HealthCore,
                new[] { new EquipmentStep(EquipmentOperation.HealthAdd, 10) }, new[] { new EquipmentStep(EquipmentOperation.EnergyAdd, 10) });
            Assert.IsFalse(EquipmentCompiler.Compile(recipe, new[] { hiddenCap }).Success);
        }

        [Test] public void CompiledCommitPreservesStateAndRejectsFailedOrUncheckedResults()
        {
            var go = new GameObject("ResolvedHost"); garbage.Add(go); var host = go.AddComponent<EquipmentHost>(); host.AutoTick = false;
            var recipe = Create<EquipmentRecipeDefinition>(); recipe.BaseStats.HitInvulnerability = 1;
            Assert.IsTrue(host.TryEquip(recipe.CreateLoadout()));
            var state = host.State; state.ApplyDamage(20); state.TrySpendEnergy(10); state.Tick(.1f, true);
            state.ApplyStatus(new StatusInfo(StatusKind.Poison, 2, 3, 0, 0));
            float health = state.Health, shield = state.Shield, energy = state.Energy, stamina = state.Stamina;
            var values = new EquipmentResolvedPart("healthcore.main", "health", "Health", EquipmentKind.HealthCore,
                new[] { new EquipmentStep(EquipmentOperation.HealthAdd, 50) }, Array.Empty<EquipmentStep>());
            var result = EquipmentCompiler.Compile(recipe, new[] { values }); Assert.IsTrue(host.TryCommitCompiled(result));
            Assert.AreSame(state, host.State); Assert.IsNull(host.Loadout); Assert.AreEqual(health, state.Health);
            Assert.AreEqual(shield, state.Shield); Assert.AreEqual(energy, state.Energy); Assert.AreEqual(stamina, state.Stamina);
            Assert.AreEqual(1, state.StatusCount); Assert.IsTrue(state.IsInvulnerable); Assert.AreEqual(1, state.Hits);
            var plan = state.Plan;
            var uncheckedResult = new EquipmentCompileResult(new CompiledEquipmentPlan("unchecked", EquipmentStats.Standard, Array.Empty<EquipmentStep>()), new List<string>());
            Assert.IsTrue(uncheckedResult.Success); Assert.IsFalse(uncheckedResult.CanCommit);
            Assert.IsFalse(host.TryCommitCompiled(uncheckedResult)); Assert.IsFalse(host.TryCommitCompiled(null));
            Assert.IsFalse(host.TryCommitCompiled(EquipmentCompiler.Compile(recipe, new[] { new EquipmentResolvedPart("healthcore.main", "invalid", "Invalid", EquipmentKind.HealthCore,
                Array.Empty<EquipmentStep>(), Array.Empty<EquipmentStep>()) })));
            Assert.AreSame(plan, state.Plan); Assert.AreEqual(health, state.Health); Assert.AreEqual(energy, state.Energy);
        }
    }
}
