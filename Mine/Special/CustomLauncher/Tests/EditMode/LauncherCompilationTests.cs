using System;
using System.Collections.Generic;
using CelToon.CustomLauncher.Compilation;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Definitions;
using CelToon.CustomLauncher.Editor;
using CelToon.CustomLauncher.Runtime;
using CelToon.CustomLauncher.Samples;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CelToon.CustomLauncher.Tests.EditMode
{
    public sealed class LauncherCompilationTests
    {
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private LauncherRecipeDefinition recipe; private ProjectileDefinition round; private DistributionDefinition fan;
        private T Create<T>() where T : ScriptableObject { var value = ScriptableObject.CreateInstance<T>(); objects.Add(value); return value; }
        [SetUp] public void Setup()
        {
            recipe = Create<LauncherRecipeDefinition>(); round = Create<ProjectileDefinition>(); round.PartId = "round";
            fan = Create<DistributionDefinition>(); fan.PartId = "fan"; fan.Mode = DistributionMode.Fan; fan.Count = 5; fan.SpreadDegrees = 30;
        }
        [TearDown] public void Cleanup() { foreach (var value in objects) if (value != null) UnityEngine.Object.DestroyImmediate(value); objects.Clear(); }
        private LauncherLoadout Loadout() => new LauncherLoadout(recipe, new PartInstance("projectile.main", round));
        private EffectPartDefinition Effect(PartKind kind, params EffectStep[] steps)
        { var effect = Create<EffectPartDefinition>(); effect.PartId = "effect." + kind; effect.Category = kind; effect.Operations = steps; return effect; }
        private static void Error(CompileResult result, string code)
        { Assert.That(result.Success, Is.False); Assert.That(result.Diagnostics, Has.Some.Matches<CompileDiagnostic>(d => d.Code == code)); }
        private sealed class Emitter : IProjectileEmitter
        { public bool Accept = true; public int Count; public bool TryEmit(IReadOnlyList<ProjectileCommand> commands) { if (!Accept) return false; Count += commands.Count; return true; } }

        [Test] public void EmptyRecipeCreatesEightOpenUniqueSlotsAndNoImplicitProjectile()
        {
            var draft = recipe.CreateLoadout(); Assert.That(draft.Parts.Length, Is.EqualTo(8));
            for (int i = 0; i < 8; i++) { Assert.That(draft.Parts[i].SlotId, Is.EqualTo(LauncherSlots.Id((PartKind)i))); Assert.That(draft.Parts[i].Part, Is.Null); }
            var result = LauncherCompiler.Compile(draft); Assert.That(result.Success, Is.True); Assert.That(result.Plan.Count, Is.Zero);
            var state = new LauncherState(); state.Equip(result.Plan); var emitter = new Emitter();
            Assert.That(LauncherFiring.TryFire(state, emitter, new FireAim(Vector3.zero, Vector3.forward, Vector3.up), 0), Is.EqualTo(FireResult.EmptyPayload));
            Assert.That(state.Energy, Is.EqualTo(100)); Assert.That(emitter.Count, Is.Zero);
        }
        [Test] public void EveryCategoryHasExactlyOneSlotRegardlessOfRecipe()
        {
            recipe.Parts = new PartDefinition[] { round, fan };
            var draft = recipe.CreateLoadout(); Assert.That(draft.Parts.Length, Is.EqualTo(8));
            Assert.That(draft.Find(PartKind.Projectile).Part, Is.SameAs(round)); Assert.That(draft.Find(PartKind.Distribution).Part, Is.SameAs(fan));
        }
        [Test] public void DuplicateCategoryInRecipeRequiresCompositePart()
        { recipe.Parts = new PartDefinition[] { fan, fan }; Error(LauncherCompiler.Compile(recipe.CreateLoadout()), "recipe.duplicate_kind"); }
        [Test] public void SameSlotCannotBeEquippedTwice()
        { Error(LauncherCompiler.Compile(new LauncherLoadout(recipe, new PartInstance("projectile.main", round), new PartInstance("projectile.main", round))), "part.duplicate_slot"); }
        [Test] public void WrongCategoryAndUnknownSlotsAreRejected()
        {
            Error(LauncherCompiler.Compile(new LauncherLoadout(recipe, new PartInstance("impact.main", fan))), "part.type");
            Error(LauncherCompiler.Compile(new LauncherLoadout(recipe, new PartInstance("projectile.second", round))), "part.unknown_slot");
        }
        [TestCase(0f)] [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidProjectileSpeedIsRejected(float value) { round.Speed = value; Error(LauncherCompiler.Compile(Loadout()), "projectile.parameters"); }
        [Test] public void ClearingSlotDoesNotInjectDefaultPartOrChangeOtherSlots()
        {
            var draft = Loadout().WithPart(PartKind.Distribution, fan); var cleared = draft.WithPart(PartKind.Distribution, null);
            Assert.That(cleared.Find(PartKind.Projectile).Part, Is.SameAs(round)); Assert.That(cleared.Find(PartKind.Distribution).Part, Is.Null);
            Assert.That(draft.Find(PartKind.Distribution).Part, Is.SameAs(fan)); Assert.That(LauncherCompiler.Compile(cleared).Plan.Count, Is.EqualTo(1));
        }
        [Test] public void EmptyImpactAddsNoBehavior()
        { Assert.That(LauncherCompiler.Compile(Loadout()).Plan.Program.Steps.Count, Is.Zero); }
        [Test] public void CompositeDoubleSplitCopiesBothOperationsInOneSlot()
        {
            var effect = Effect(PartKind.Trigger, new EffectStep(EffectOperation.TimedSplit, 20, .8f, 3, .2f), new EffectStep(EffectOperation.TimedSplit, 40, .7f, 3, .4f));
            var draft = Loadout().WithPart(PartKind.Trigger, effect); var plan = LauncherCompiler.Compile(draft).Plan;
            Assert.That(plan.Program.Steps.Count, Is.EqualTo(2)); Assert.That(draft.Parts.Length, Is.EqualTo(8));
            effect.Operations[0] = new EffectStep(EffectOperation.Burst); Assert.That(plan.Program.Steps[0].Operation, Is.EqualTo(EffectOperation.TimedSplit));
        }
        [Test] public void WrongOperationCategoryIsRejected()
        { Error(LauncherCompiler.Compile(Loadout().WithPart(PartKind.Damage, Effect(PartKind.Damage, new EffectStep(EffectOperation.Explosion, 3, 1)))), "effect.operation"); }
        [Test] public void InvalidOperationParameterIsRejected()
        { Error(LauncherCompiler.Compile(Loadout().WithPart(PartKind.Trigger, Effect(PartKind.Trigger, new EffectStep(EffectOperation.TimedSplit, 20, 1, 3, float.NaN)))), "effect.operation"); }
        [Test] public void GeneratedBudgetIncludesChildrenAndBurst()
        {
            recipe.MaxGeneratedPerShot = 10;
            var split = Effect(PartKind.Trigger, new EffectStep(EffectOperation.TimedSplit, 20, 1, 3, .2f));
            Error(LauncherCompiler.Compile(Loadout().WithPart(PartKind.Distribution, fan).WithPart(PartKind.Trigger, split)), "budget.generated");
        }
        [Test] public void ModifiersCombineWithoutMutatingDefinitions()
        {
            var tune = Effect(PartKind.Modifier, new EffectStep(EffectOperation.DamageScale, 1.4f), new EffectStep(EffectOperation.DamageScale, 1.4f));
            var plan = LauncherCompiler.Compile(Loadout().WithPart(PartKind.Modifier, tune)).Plan;
            Assert.That(plan.Projectile.Damage, Is.EqualTo(round.Damage * 1.96f).Within(.001)); Assert.That(round.Damage, Is.EqualTo(10));
        }
        [Test] public void BurstReservesAllCommandsAndCooldownCoversLastEmission()
        {
            var control = Effect(PartKind.FireControl, new EffectStep(EffectOperation.Burst, 1, 1, 3, .15f));
            var plan = LauncherCompiler.Compile(Loadout().WithPart(PartKind.Distribution, fan).WithPart(PartKind.FireControl, control)).Plan;
            var commands = LauncherFiring.BuildCommands(plan, new FireAim(Vector3.zero, Vector3.forward, Vector3.up));
            Assert.That(commands.Length, Is.EqualTo(15)); Assert.That(commands[10].Delay, Is.EqualTo(.3f).Within(.001));
            Assert.That(plan.Cooldown, Is.GreaterThan(.3f));
        }
        [Test] public void SlotArrayOrderDoesNotAlterPipeline()
        {
            var a = Loadout().WithPart(PartKind.Distribution, fan); var b = new LauncherLoadout(recipe, new PartInstance("distribution.main", fan), new PartInstance("projectile.main", round));
            var pa = LauncherCompiler.Compile(a).Plan; var pb = LauncherCompiler.Compile(b).Plan;
            Assert.That(pa.Stages, Is.EqualTo(pb.Stages)); Assert.That(pa.Count, Is.EqualTo(pb.Count));
            Assert.That(pa.Stages[1], Is.EqualTo(LauncherStage.Modifier));
        }
        [Test] public void FailedSelectionPreservesPlanEnergyAndCooldown()
        {
            var go = new GameObject("Workbench"); objects.Add(go); var host = go.AddComponent<LauncherHost>(); var ui = go.AddComponent<LauncherWorkbenchUI>(); ui.Host = host;
            host.TryEquip(Loadout()); var old = host.State.Plan; var aim = new FireAim(Vector3.zero, Vector3.forward, Vector3.up);
            host.TryFire(new Emitter(), aim, 0); float energy = host.State.Energy;
            ui.OpenSlot(PartKind.Projectile); Assert.That(ui.SelectPart(PartKind.Projectile, fan), Is.False);
            Assert.That(host.State.Plan, Is.SameAs(old)); Assert.That(host.State.Energy, Is.EqualTo(energy)); Assert.That(host.State.NextFireTime, Is.EqualTo(old.Cooldown));
        }
        [Test] public void PanelOpensCategoryAndEmptySelectionClearsIt()
        {
            var go = new GameObject("Workbench"); objects.Add(go); var host = go.AddComponent<LauncherHost>(); var ui = go.AddComponent<LauncherWorkbenchUI>(); ui.Host = host;
            host.TryEquip(Loadout()); ui.OpenSlot(PartKind.Projectile); Assert.That(ui.PopupOpen, Is.True);
            Assert.That(ui.SelectPart(PartKind.Projectile, null), Is.True); Assert.That(ui.PopupOpen, Is.False); Assert.That(ui.CurrentPart(PartKind.Projectile), Is.Null);
        }
        [Test] public void CapacityFailureDoesNotSpendResources()
        {
            var state = new LauncherState(); state.Equip(LauncherCompiler.Compile(Loadout()).Plan);
            Assert.That(LauncherFiring.TryFire(state, new Emitter { Accept = false }, new FireAim(Vector3.zero, Vector3.forward, Vector3.up), 0), Is.EqualTo(FireResult.CapacityExceeded));
            Assert.That(state.ShotCount, Is.Zero); Assert.That(state.Energy, Is.EqualTo(100));
        }
        [Test] public void CatalogHasUniqueIdsAndEveryPartAndRecipeCompiles()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PartCatalog>(RangeSceneBuilder.RootPath + "/Samples/Definitions/Catalog.asset");
            Assert.That(catalog, Is.Not.Null); Assert.That(catalog.Parts.Length, Is.GreaterThanOrEqualTo(60)); var ids = new HashSet<string>();
            foreach (var part in catalog.Parts)
            {
                Assert.That(ids.Add(part.PartId), Is.True, part.name); var draft = Loadout().WithPart(part.Kind, part);
                var result = LauncherCompiler.Compile(draft); Assert.That(result.Success, Is.True, part.name + ": " + (result.Diagnostics.Count > 0 ? result.Diagnostics[0].ToString() : ""));
            }
            foreach (var r in catalog.Recipes) Assert.That(LauncherCompiler.Compile(r.CreateLoadout()).Success, Is.True, r.name);
        }
    }
}
