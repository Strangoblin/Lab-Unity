using System;
using System.Collections.Generic;
using System.Linq;
using CelToon.CustomEquipment.Compilation;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Definitions;
using CelToon.CustomEquipment.Editor;
using CelToon.CustomEquipment.Runtime;
using CelToon.CustomEquipment.Samples;
using CelToon.CustomLauncher.Contracts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CelToon.CustomLauncher.Runtime;
using CelToon.CustomLauncher.Projectiles;

namespace CelToon.CustomEquipment.Tests.EditMode
{
    public sealed class EquipmentTests
    {
        private readonly List<UnityEngine.Object> garbage = new List<UnityEngine.Object>();
        private const string MigrationTestFolder = "Assets/Editor/EquipmentCoreMigrationTestAssets";
        private EquipmentRecipeDefinition Recipe()
        { var r = ScriptableObject.CreateInstance<EquipmentRecipeDefinition>(); garbage.Add(r); return r; }
        private EquipmentPartDefinition Part(EquipmentKind kind, params EquipmentStep[] steps)
        {
            var p = ScriptableObject.CreateInstance<EquipmentPartDefinition>(); garbage.Add(p);
            p.PartId = Guid.NewGuid().ToString(); p.Kind = kind;
            if (EquipmentSlots.Legacy(kind)) p.Operations = steps;
            else {
                p.PrimaryOperations = steps.Where(step => EquipmentSlots.IsPrimary(step.Operation)).ToArray();
                p.Affixes = steps.Where(step => !EquipmentSlots.IsPrimary(step.Operation)).ToArray();
                if (p.PrimaryOperations.Length == 0) p.PrimaryOperations = new[] { new EquipmentStep(EquipmentSlots.PrimaryOperation(kind), 0) };
            }
            return p;
        }
        private EquipmentState State(EquipmentStats stats)
        { var s = new EquipmentState(); s.Equip(new CompiledEquipmentPlan("test", stats, Array.Empty<EquipmentStep>())); return s; }
        [TearDown] public void Cleanup() {
            foreach (var obj in garbage) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); garbage.Clear();
            if (AssetDatabase.IsValidFolder(MigrationTestFolder)) AssetDatabase.DeleteAsset(MigrationTestFolder);
        }
        [Test] public void LegacyNumericValuesArePreservedAndOnlyFourCoreKindsAreSupported()
        {
            Assert.AreEqual(0, (int)EquipmentKind.Vitality); Assert.AreEqual(7, (int)EquipmentKind.Utility);
            for (int i = 0; i < 12; i++) Assert.IsFalse(EquipmentSlots.Supported((EquipmentKind)i));
            for (int i = 0; i < 4; i++) Assert.AreEqual(i, EquipmentSlots.Index(EquipmentSlots.At(i)));
            Assert.AreEqual(-1, EquipmentSlots.Index((EquipmentKind)999));
        }
        [Test] public void DefenseCoreCombinesHealthArmorAndResistance()
        {
            var core = Part(EquipmentKind.HealthCore, new EquipmentStep(EquipmentOperation.HealthAdd, 40),
                new EquipmentStep(EquipmentOperation.ArmorAdd, 6), new EquipmentStep(EquipmentOperation.BurnResistance, .5f));
            var result = EquipmentCompiler.Compile(Recipe().CreateLoadout().WithPart(core.Kind, core));
            Assert.IsTrue(result.Success); Assert.AreEqual(140, result.Plan.Stats.MaxHealth);
            Assert.AreEqual(6, result.Plan.Stats.Armor); Assert.AreEqual(.5f, result.Plan.Stats.BurnResistance);
        }
        [Test] public void LegacyLoadoutBecomesFourCoresAndSplitUtilityEffectsSurvive()
        {
            var recipe = Recipe();
            var vitality = Part(EquipmentKind.Vitality, new EquipmentStep(EquipmentOperation.HealthScale, 1.3f), new EquipmentStep(EquipmentOperation.HealthAdd, 30));
            var armor = Part(EquipmentKind.Armor, new EquipmentStep(EquipmentOperation.ArmorAdd, 6), new EquipmentStep(EquipmentOperation.DamageScale, .85f));
            var shield = Part(EquipmentKind.Shield, new EquipmentStep(EquipmentOperation.ShieldAdd, 50), new EquipmentStep(EquipmentOperation.ShieldRegen, 8));
            var utility = Part(EquipmentKind.Utility, new EquipmentStep(EquipmentOperation.StaminaAdd, 30), new EquipmentStep(EquipmentOperation.CleanseInterval, 3));
            var old = new EquipmentLoadout(recipe, new EquipmentPartInstance("vitality.main", vitality), new EquipmentPartInstance("armor.main", armor),
                new EquipmentPartInstance("shield.main", shield), new EquipmentPartInstance("utility.main", utility));
            var upgraded = EquipmentCoreMigration.UpgradeLoadout(old, MigrationTestFolder);
            Assert.AreEqual(4, upgraded.Parts.Length); var result = EquipmentCompiler.Compile(upgraded); Assert.IsTrue(result.Success);
            var stats = result.Plan.Stats; Assert.AreEqual(169, stats.MaxHealth, .001); Assert.AreEqual(6, stats.Armor); Assert.AreEqual(.85f, stats.DamageScale);
            Assert.AreEqual(50, stats.MaxShield); Assert.AreEqual(8, stats.ShieldRegen); Assert.AreEqual(130, stats.MaxStamina); Assert.AreEqual(3, stats.CleanseInterval);
            Assert.AreSame(upgraded, EquipmentCoreMigration.UpgradeLoadout(upgraded, MigrationTestFolder));
            var repeated = EquipmentCoreMigration.UpgradeLoadout(old, MigrationTestFolder);
            Assert.AreSame(upgraded.Find(EquipmentKind.HealthCore).Part, repeated.Find(EquipmentKind.HealthCore).Part);
            var cleared = upgraded.WithPart(EquipmentKind.HealthCore, null); var after = EquipmentCompiler.Compile(cleared).Plan.Stats;
            Assert.AreEqual(100, after.MaxHealth); Assert.AreEqual(0, after.Armor); Assert.AreEqual(1, after.DamageScale);
            Assert.AreEqual(50, after.MaxShield); Assert.AreEqual(130, after.MaxStamina); Assert.AreEqual(0, after.CleanseInterval);
            Assert.AreSame(vitality, old.Parts[0].Part); Assert.AreEqual("vitality.main", old.Parts[0].SlotId);
        }
        [Test] public void InvalidLegacySlotDoesNotGetSilentlyDropped()
        {
            var old = new EquipmentLoadout(Recipe(), new EquipmentPartInstance("vitality.main", null), new EquipmentPartInstance("unknown.main", null));
            Assert.Throws<InvalidOperationException>(() => EquipmentCoreMigration.UpgradeLoadout(old, MigrationTestFolder));
            Assert.AreEqual("unknown.main", old.Parts[1].SlotId);
        }
        [Test] public void FourCoreMigrationPreservesLiveResourcesAndStatuses()
        {
            var root = new GameObject("Host"); garbage.Add(root); var host = root.AddComponent<EquipmentHost>(); var recipe = Recipe();
            Assert.IsTrue(host.TryEquip(recipe.CreateLoadout())); host.State.ApplyDamage(25);
            host.State.ApplyStatus(new StatusInfo(StatusKind.Poison, 1, 5, 0, 0));
            var part = Part(EquipmentKind.Vitality, new EquipmentStep(EquipmentOperation.HealthAdd, 40));
            var upgraded = EquipmentCoreMigration.UpgradeLoadout(new EquipmentLoadout(recipe, new EquipmentPartInstance("vitality.main", part)), MigrationTestFolder);
            Assert.IsTrue(host.TryEquip(upgraded)); Assert.AreEqual(75, host.State.Health); Assert.AreEqual(1, host.State.StatusCount);
        }
        [Test] public void EmptyRecipeHasFourOpenSlotsAndStandardStats()
        {
            var l = Recipe().CreateLoadout(); Assert.AreEqual(4, l.Parts.Length);
            foreach (var p in l.Parts) Assert.IsNull(p.Part);
            var result = EquipmentCompiler.Compile(l); Assert.IsTrue(result.Success); Assert.AreEqual(100, result.Plan.Stats.MaxHealth);
        }
        [Test] public void ResourceMainStatsMatchExactlyOneSlotAndAffixesCannotAddOtherCaps()
        {
            var kinds = new[] { EquipmentKind.HealthCore, EquipmentKind.ShieldCore, EquipmentKind.StaminaCore, EquipmentKind.EnergyCore };
            foreach (var kind in kinds) {
                var part = Part(kind, new EquipmentStep(EquipmentSlots.PrimaryOperation(kind), 20));
                Assert.IsTrue(EquipmentCompiler.Compile(Recipe().CreateLoadout().WithPart(kind, part)).Success);
                part.Affixes = new[] { new EquipmentStep(EquipmentOperation.EnergyAdd, 20) };
                Assert.IsFalse(EquipmentCompiler.Compile(Recipe().CreateLoadout().WithPart(kind, part)).Success);
            }
            var wrong = Part(EquipmentKind.EnergyCore, new EquipmentStep(EquipmentOperation.HealthAdd, 20));
            Assert.IsFalse(EquipmentCompiler.Compile(Recipe().CreateLoadout().WithPart(wrong.Kind, wrong)).Success);
        }
        [Test] public void SecondaryEffectsCanLiveOnAnyResourceCore()
        {
            var part = Part(EquipmentKind.ShieldCore, new EquipmentStep(EquipmentOperation.ShieldAdd, 50),
                new EquipmentStep(EquipmentOperation.ArmorAdd, 6), new EquipmentStep(EquipmentOperation.HealthRegen, 3),
                new EquipmentStep(EquipmentOperation.MoveScale, 1.2f), new EquipmentStep(EquipmentOperation.EnergyCostScale, .75f));
            var compiled = EquipmentCompiler.Compile(Recipe().CreateLoadout().WithPart(part.Kind, part));
            Assert.IsTrue(compiled.Success); Assert.AreEqual(50, compiled.Plan.Stats.MaxShield);
            Assert.AreEqual(6, compiled.Plan.Stats.Armor); Assert.AreEqual(3, compiled.Plan.Stats.HealthRegen);
            Assert.AreEqual(3.6f, compiled.Plan.Stats.MoveSpeed, .001); Assert.AreEqual(.75f, compiled.Plan.Stats.EnergyCostScale);
        }
        [Test] public void EnergyCoreCompilesCapacityRecoveryAndCostAndSpendUsesThem()
        {
            var part = Part(EquipmentKind.EnergyCore, new EquipmentStep(EquipmentOperation.EnergyAdd, 50),
                new EquipmentStep(EquipmentOperation.EnergyRegen, 5), new EquipmentStep(EquipmentOperation.EnergyCostScale, .5f));
            var compiled = EquipmentCompiler.Compile(Recipe().CreateLoadout().WithPart(part.Kind, part)); Assert.IsTrue(compiled.Success);
            var state = new EquipmentState(); state.Equip(compiled.Plan); Assert.AreEqual(150, state.Energy);
            Assert.IsTrue(state.TrySpendEnergy(100)); Assert.AreEqual(100, state.Energy); state.Tick(1);
            Assert.AreEqual(115, state.Energy, .01); Assert.IsTrue(state.RechargeEnergy(100)); Assert.AreEqual(150, state.Energy);
        }
        [Test] public void FailedEnergySpendAndRechargeAreAtomic()
        {
            var state = State(EquipmentStats.Standard);
            foreach (float cost in new[] { float.NaN, float.PositiveInfinity, -1, 101 }) {
                Assert.IsFalse(state.TrySpendEnergy(cost)); Assert.AreEqual(100, state.Energy);
            }
            Assert.IsFalse(state.RechargeEnergy(float.NaN)); Assert.IsFalse(state.RechargeEnergy(-1)); Assert.AreEqual(100, state.Energy);
        }
        [Test] public void EnergySwapPreservesCurrentAmountClampsLowerCapAndResetIsExplicit()
        {
            var state = State(EquipmentStats.Standard); Assert.IsTrue(state.TrySpendEnergy(30));
            var stats = EquipmentStats.Standard; stats.MaxEnergy = 200;
            state.Equip(new CompiledEquipmentPlan("larger", stats, Array.Empty<EquipmentStep>())); Assert.AreEqual(70, state.Energy);
            stats.MaxEnergy = 50; state.Equip(new CompiledEquipmentPlan("smaller", stats, Array.Empty<EquipmentStep>())); Assert.AreEqual(50, state.Energy);
            Assert.IsTrue(state.TrySpendEnergy(20)); state.Reset(); Assert.AreEqual(50, state.Energy);
        }
        [Test] public void DeadPlayerCannotSpendOrRegenerateEnergy()
        {
            var state = State(EquipmentStats.Standard); state.TrySpendEnergy(30); state.ApplyDamage(100);
            Assert.IsFalse(state.TrySpendEnergy(10)); Assert.IsFalse(state.RechargeEnergy(10)); state.Tick(1); Assert.AreEqual(70, state.Energy);
        }
        [Test] public void PreviousFourCoreLoadoutMovesEmergencyEffectsToLifeAffixes()
        {
            var defense = Part(EquipmentKind.DefenseCore, new EquipmentStep(EquipmentOperation.HealthAdd, 40), new EquipmentStep(EquipmentOperation.ArmorAdd, 6));
            var emergency = Part(EquipmentKind.EmergencyDevice, new EquipmentStep(EquipmentOperation.HealthRegen, 8), new EquipmentStep(EquipmentOperation.CleanseInterval, 3));
            var old = new EquipmentLoadout(Recipe(), new EquipmentPartInstance("defensecore.main", defense), new EquipmentPartInstance("emergencydevice.main", emergency));
            var upgraded = EquipmentCoreMigration.UpgradeLoadout(old, MigrationTestFolder);
            var core = upgraded.Find(EquipmentKind.HealthCore).Part;
            Assert.AreEqual(1, core.PrimaryOperations.Length); Assert.AreEqual(3, core.Affixes.Length);
            var compiled = EquipmentCompiler.Compile(upgraded); Assert.IsTrue(compiled.Success);
            Assert.AreEqual(140, compiled.Plan.Stats.MaxHealth); Assert.AreEqual(8, compiled.Plan.Stats.HealthRegen);
            Assert.AreEqual(3, compiled.Plan.Stats.CleanseInterval); Assert.IsNull(upgraded.Find(EquipmentKind.EnergyCore).Part);
            Assert.AreEqual("defensecore.main", old.Parts[0].SlotId);
        }
        [Test] public void LethalDamageOverTimeStopsEnergyRecoveryInThatSubstep()
        {
            var stats = EquipmentStats.Standard; stats.MaxHealth = 1;
            var state = State(stats); state.TrySpendEnergy(50);
            state.ApplyStatus(new StatusInfo(StatusKind.Burn, 100, 1, 0, 0)); state.Tick(.02f);
            Assert.IsTrue(state.IsDead); Assert.AreEqual(50, state.Energy);
        }
        [Test] public void LegacyAffixOnlyCoreReceivesNeutralMainStatWithoutFreeHealth()
        {
            var old = new EquipmentLoadout(Recipe(), new EquipmentPartInstance("emergencydevice.main",
                Part(EquipmentKind.EmergencyDevice, new EquipmentStep(EquipmentOperation.HitInvulnerability, .7f))));
            var upgraded = EquipmentCoreMigration.UpgradeLoadout(old, MigrationTestFolder);
            var core = upgraded.Find(EquipmentKind.HealthCore).Part; Assert.AreEqual(0, core.PrimaryOperations[0].A);
            var result = EquipmentCompiler.Compile(upgraded); Assert.IsTrue(result.Success);
            Assert.AreEqual(100, result.Plan.Stats.MaxHealth); Assert.AreEqual(.7f, result.Plan.Stats.HitInvulnerability);
        }
        [Test] public void FullLegacyAffixBudgetSurvivesNeutralPrimaryMigration()
        {
            var defense = Part(EquipmentKind.DefenseCore, Enumerable.Repeat(new EquipmentStep(EquipmentOperation.ArmorAdd, .1f), 24).ToArray());
            var mobility = Part(EquipmentKind.MobilityDrive, Enumerable.Repeat(new EquipmentStep(EquipmentOperation.SlowResistance, .01f), 16).ToArray());
            var emergency = Part(EquipmentKind.EmergencyDevice, Enumerable.Repeat(new EquipmentStep(EquipmentOperation.HealthRegen, .2f), 24).ToArray());
            var old = new EquipmentLoadout(Recipe(), new EquipmentPartInstance("defensecore.main", defense),
                new EquipmentPartInstance("mobilitydrive.main", mobility), new EquipmentPartInstance("emergencydevice.main", emergency));
            var upgraded = EquipmentCoreMigration.UpgradeLoadout(old, MigrationTestFolder);
            var core = upgraded.Find(EquipmentKind.HealthCore).Part;
            Assert.AreEqual(1, core.PrimaryOperations.Length); Assert.AreEqual(0, core.PrimaryOperations[0].A);
            Assert.AreEqual(64, core.Affixes.Length);
            CollectionAssert.AreEqual(defense.Operations.Concat(mobility.Operations).Concat(emergency.Operations), core.Affixes);
            var result = EquipmentCompiler.Compile(upgraded); Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
            Assert.AreEqual(65, result.Plan.Steps.Count); Assert.AreEqual(100, result.Plan.Stats.MaxHealth);
            Assert.AreEqual(2.4f, result.Plan.Stats.Armor, .001f); Assert.AreEqual(4.8f, result.Plan.Stats.HealthRegen, .001f);
            Assert.AreEqual(1 - Mathf.Pow(.99f, 16), result.Plan.Stats.SlowResistance, .001f);
            var repeated = EquipmentCoreMigration.UpgradeLoadout(old, MigrationTestFolder);
            Assert.AreSame(core, repeated.Find(EquipmentKind.HealthCore).Part);
        }
        [Test] public void EmptyLegacyRecipeGetsEnergyDefaultsOnceAndZeroCapacityCanBeAuthored()
        {
            if (!AssetDatabase.IsValidFolder(MigrationTestFolder)) AssetDatabase.CreateFolder("Assets/Editor", "EquipmentCoreMigrationTestAssets");
            var recipe = ScriptableObject.CreateInstance<EquipmentRecipeDefinition>(); recipe.BaseStats.MaxEnergy = 0;
            recipe.BaseStats.EnergyRegen = 0; recipe.BaseStats.EnergyCostScale = 0;
            AssetDatabase.CreateAsset(recipe, MigrationTestFolder + "/Empty.asset");
            Assert.IsTrue(EquipmentCoreMigration.UpgradeRecipe(recipe)); Assert.AreEqual(100, recipe.BaseStats.MaxEnergy);
            Assert.AreEqual(10, recipe.BaseStats.EnergyRegen); Assert.AreEqual(1, recipe.BaseStats.EnergyCostScale);
            Assert.IsFalse(EquipmentCoreMigration.UpgradeRecipe(recipe));
            recipe.BaseStats.MaxEnergy = 0; recipe.BaseStats.EnergyRegen = 0;
            Assert.IsFalse(EquipmentCoreMigration.UpgradeRecipe(recipe)); Assert.AreEqual(0, recipe.BaseStats.MaxEnergy);
            Assert.IsTrue(EquipmentCompiler.Compile(recipe.CreateLoadout()).Success);
        }
        [TestCase(float.NaN)] [TestCase(0)] [TestCase(5)] public void InvalidEnergyCostScaleRejectsCompilation(float scale)
        {
            var recipe = Recipe(); recipe.BaseStats.EnergyCostScale = scale;
            Assert.IsFalse(EquipmentCompiler.Compile(recipe.CreateLoadout()).Success);
        }
        [Test] public void DuplicateSlotsRejected()
        {
            var r = Recipe(); var slot = new EquipmentPartInstance("healthcore.main", null);
            Assert.IsFalse(EquipmentCompiler.Compile(new EquipmentLoadout(r, slot, slot)).Success);
        }
        [Test] public void UnknownSlotRejected() => Assert.IsFalse(EquipmentCompiler.Compile(new EquipmentLoadout(Recipe(), new EquipmentPartInstance("other.main", null))).Success);
        [Test] public void WrongKindRejected()
        {
            var p = Part(EquipmentKind.ShieldCore, new EquipmentStep(EquipmentOperation.ShieldAdd, 2));
            Assert.IsFalse(EquipmentCompiler.Compile(Recipe().CreateLoadout().WithPart(EquipmentKind.HealthCore, p)).Success);
        }
        [Test] public void DuplicateRecipeCategoriesRejected()
        {
            var r = Recipe(); r.Parts = new[] { Part(EquipmentKind.HealthCore, new EquipmentStep(EquipmentOperation.HealthAdd, 1)), Part(EquipmentKind.HealthCore, new EquipmentStep(EquipmentOperation.HealthAdd, 2)) };
            Assert.IsFalse(EquipmentCompiler.Compile(r.CreateLoadout()).Success);
        }
        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(-1)] [TestCase(10001)]
        public void InvalidBaseHealthRejected(float health)
        { var r = Recipe(); r.BaseStats.MaxHealth = health; Assert.IsFalse(EquipmentCompiler.Compile(r.CreateLoadout()).Success); }
        [Test] public void IncompatibleCoreOperationRejected()
        {
            var p = Part(EquipmentKind.HealthCore, new EquipmentStep(EquipmentOperation.ShieldAdd, 20));
            Assert.IsFalse(EquipmentCompiler.Compile(Recipe().CreateLoadout().WithPart(p.Kind, p)).Success);
        }
        [Test] public void InvalidOperationEnumRejected()
        {
            var p = Part(EquipmentKind.EnergyCore, new EquipmentStep((EquipmentOperation)999, 1));
            Assert.IsFalse(EquipmentCompiler.Compile(Recipe().CreateLoadout().WithPart(p.Kind, p)).Success);
        }
        [Test] public void OversizedCompositeRejected()
        {
            var steps = new EquipmentStep[EquipmentSlots.MaxOperationsPerPart + 1]; for (int i = 0; i < steps.Length; i++) steps[i] = new EquipmentStep(EquipmentOperation.HealthAdd, 1);
            var p = Part(EquipmentKind.HealthCore, steps); Assert.IsFalse(EquipmentCompiler.Compile(Recipe().CreateLoadout().WithPart(p.Kind, p)).Success);
        }
        [Test] public void AdditionsPrecedeHealthMultipliersAndSnapshotIsIndependent()
        {
            var p = Part(EquipmentKind.HealthCore, new EquipmentStep(EquipmentOperation.HealthScale, 1.3f), new EquipmentStep(EquipmentOperation.HealthAdd, 30));
            var result = EquipmentCompiler.Compile(Recipe().CreateLoadout().WithPart(p.Kind, p));
            Assert.IsTrue(result.Success); Assert.AreEqual(169, result.Plan.Stats.MaxHealth, .001);
            p.PrimaryOperations[0].A = 3; Assert.AreEqual(169, result.Plan.Stats.MaxHealth, .001); Assert.AreEqual(1.3f, result.Plan.Steps[0].A);
        }
        [Test] public void ClearPartReturnsToBaseWithoutMutatingOriginal()
        {
            var p = Part(EquipmentKind.HealthCore, new EquipmentStep(EquipmentOperation.HealthAdd, 50));
            var before = Recipe().CreateLoadout().WithPart(p.Kind, p); var after = before.WithPart(p.Kind, null);
            Assert.AreSame(p, before.Find(p.Kind).Part); Assert.IsNull(after.Find(p.Kind).Part);
            Assert.AreEqual(100, EquipmentCompiler.Compile(after).Plan.Stats.MaxHealth);
        }
        [Test] public void ArmorThenReductionThenShieldThenHealth()
        {
            var stats = EquipmentStats.Standard; stats.Armor = 5; stats.DamageScale = .8f; stats.MaxShield = 10;
            var state = State(stats); var report = state.ApplyDamage(25);
            Assert.AreEqual(9, report.Mitigated, .001); Assert.AreEqual(10, report.ShieldLost, .001); Assert.AreEqual(6, report.HealthLost, .001);
            Assert.AreEqual(94, state.Health, .001);
        }
        [Test] public void DotUsesElementResistanceAndBypassesFlatArmor()
        {
            var stats = EquipmentStats.Standard; stats.Armor = 100; stats.BurnResistance = .5f;
            var state = State(stats); state.ApplyStatus(new StatusInfo(StatusKind.Burn, 10, 2, 0, 0)); state.Tick(1);
            Assert.AreEqual(95, state.Health, .01);
        }
        [Test] public void FirePoisonIndependentAndRepeatedStatusRefreshesWithoutStacking()
        {
            var state = State(EquipmentStats.Standard);
            state.ApplyStatus(new StatusInfo(StatusKind.Burn, 5, 2, 0, 0)); state.ApplyStatus(new StatusInfo(StatusKind.Burn, 3, 1, 0, 0));
            state.ApplyStatus(new StatusInfo(StatusKind.Poison, 4, 2, 0, 0)); state.Tick(1);
            Assert.AreEqual(91, state.Health, .01); Assert.AreEqual(2, state.StatusCount);
        }
        [Test] public void DotOnlyAccountsForRemainingDuration()
        { var state = State(EquipmentStats.Standard); state.ApplyStatus(new StatusInfo(StatusKind.Burn, 10, .1f, 0, 0)); state.Tick(1); Assert.AreEqual(99, state.Health, .01); Assert.AreEqual(0, state.StatusCount); }
        [Test] public void SlowStunAndMarkHaveActualEffects()
        {
            var stats = EquipmentStats.Standard; stats.SlowResistance = .5f; stats.StunResistance = .5f; stats.MarkResistance = .5f;
            var state = State(stats); state.ApplyStatus(new StatusInfo(StatusKind.Slow, .2f, 4, 0, 0)); Assert.AreEqual(1.8f, state.MoveSpeed, .001);
            state.ApplyStatus(new StatusInfo(StatusKind.Stun, 1, 1, 0, 0)); Assert.AreEqual(0, state.MoveSpeed); Assert.AreEqual(.5f, state.StatusRemaining(StatusKind.Stun), .001);
            state.ApplyStatus(new StatusInfo(StatusKind.Mark, 2, 2, 0, 0)); state.ApplyDamage(10); Assert.AreEqual(85, state.Health, .001);
            state.Tick(.6f); Assert.AreEqual(1.8f, state.MoveSpeed, .001);
        }
        [Test] public void EquipPreservesResourcesStatusesAndInvulnerability()
        {
            var stats = EquipmentStats.Standard; stats.MaxShield = 20; stats.HitInvulnerability = 1;
            var state = State(stats); state.ApplyDamage(30); state.ApplyStatus(new StatusInfo(StatusKind.Poison, 1, 2, 0, 0));
            stats.MaxHealth = 200; stats.MaxShield = 80; state.Equip(new CompiledEquipmentPlan("other", stats, Array.Empty<EquipmentStep>()));
            Assert.AreEqual(90, state.Health); Assert.AreEqual(0, state.Shield); Assert.AreEqual(1, state.StatusCount);
            Assert.IsTrue(state.ApplyDamage(10).Ignored); state.Tick(.5f); Assert.Less(state.Health, 90);
        }
        [Test] public void LowerCapsClampAndRaisingAgainDoesNotRefill()
        {
            var stats = EquipmentStats.Standard; stats.MaxShield = 100; var state = State(stats);
            stats.MaxShield = 30; stats.MaxStamina = 40; state.Equip(new CompiledEquipmentPlan("small", stats, Array.Empty<EquipmentStep>()));
            stats.MaxShield = 100; stats.MaxStamina = 100; state.Equip(new CompiledEquipmentPlan("big", stats, Array.Empty<EquipmentStep>()));
            Assert.AreEqual(30, state.Shield); Assert.AreEqual(40, state.Stamina);
        }
        [Test] public void RegenHonorsDelayAndDamageRestartsIt()
        {
            var stats = EquipmentStats.Standard; stats.MaxShield = 10; stats.ShieldRegen = 5; stats.ShieldDelay = 1; stats.HealthRegen = 2; stats.RecoveryDelay = 1;
            var state = State(stats); state.ApplyDamage(20); state.Tick(.5f); Assert.AreEqual(0, state.Shield); Assert.AreEqual(90, state.Health);
            state.ApplyDamage(5); state.Tick(1.5f); Assert.AreEqual(2.5f, state.Shield, .02); Assert.AreEqual(86, state.Health, .02);
        }
        [Test] public void InvulnerabilityProtectsDirectButNotDot()
        {
            var stats = EquipmentStats.Standard; stats.HitInvulnerability = .5f; var state = State(stats);
            state.ApplyDamage(10); Assert.IsTrue(state.ApplyDamage(10).Ignored); state.ApplyDamage(5, DamageChannel.Burn); Assert.AreEqual(85, state.Health);
            state.Tick(.6f); Assert.IsFalse(state.ApplyDamage(10).Ignored);
        }
        [Test] public void EmergencyHealHasCooldownAndCannotRevive()
        {
            var stats = EquipmentStats.Standard; stats.EmergencyHealFraction = .2f; stats.EmergencyCooldown = 5;
            var state = State(stats); state.ApplyDamage(75); Assert.AreEqual(45, state.Health); Assert.AreEqual(1, state.EmergencyActivations);
            state.ApplyDamage(20); Assert.AreEqual(25, state.Health); Assert.AreEqual(1, state.EmergencyActivations);
            state.Tick(5.1f); state.ApplyDamage(1); Assert.AreEqual(44, state.Health);
            state.ApplyDamage(1000); state.Tick(10); Assert.IsTrue(state.IsDead); Assert.AreEqual(0, state.Health);
        }
        [Test] public void CleanseClockPersistsThroughEquip()
        {
            var stats = EquipmentStats.Standard; stats.CleanseInterval = 3; var state = State(stats); state.Tick(2);
            state.Equip(new CompiledEquipmentPlan("other", stats, Array.Empty<EquipmentStep>()));
            state.ApplyStatus(new StatusInfo(StatusKind.Slow, .5f, 10, 0, 0)); state.Tick(1.1f); Assert.AreEqual(0, state.StatusCount);
        }
        [Test] public void SprintConsumesStaminaAndRecoversAfterExhaustion()
        {
            var stats = EquipmentStats.Standard; stats.MaxStamina = 10; stats.SprintCost = 10; stats.StaminaRegen = 5;
            var state = State(stats); state.Tick(.5f, true); Assert.AreEqual(5, state.Stamina, .02); Assert.AreEqual(5.1f, state.MoveSpeed, .001);
            state.Tick(.6f, true); Assert.IsFalse(state.Sprinting); state.Tick(.5f, false); Assert.Greater(state.Stamina, 2.5f);
            state.Tick(.02f, true); Assert.IsTrue(state.Sprinting);
        }
        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(-1)]
        public void InvalidDamageAndTickDoNotCorruptState(float value)
        { var state = State(EquipmentStats.Standard); Assert.IsTrue(state.ApplyDamage(value).Ignored); state.Tick(value); Assert.AreEqual(100, state.Health); }
        [Test] public void FailedEquipIsAtomicAndCallerCannotMutateSavedLoadout()
        {
            var go = new GameObject("Host"); garbage.Add(go); var host = go.AddComponent<EquipmentHost>(); var draft = Recipe().CreateLoadout();
            Assert.IsTrue(host.TryEquip(draft)); var plan = host.State.Plan; draft.Parts[0].SlotId = "invalid";
            Assert.AreEqual("healthcore.main", host.Loadout.Parts[0].SlotId); Assert.IsFalse(host.TryEquip(draft)); Assert.AreSame(plan, host.State.Plan);
            var returned = host.Loadout; returned.Parts[0].SlotId = "mutated"; Assert.AreEqual("healthcore.main", host.Loadout.Parts[0].SlotId);
        }
        [Test] public void WorkbenchOpensClearsAndRejectsWrongCategory()
        {
            var go = new GameObject("Workbench"); garbage.Add(go); var host = go.AddComponent<EquipmentHost>(); var ui = go.AddComponent<EquipmentWorkbenchUI>(); ui.Host = host;
            ui.SelectRecipe(Recipe()); ui.OpenSlot(EquipmentKind.HealthCore); Assert.IsTrue(ui.PopupOpen);
            var p = Part(EquipmentKind.HealthCore, new EquipmentStep(EquipmentOperation.HealthAdd, 20)); Assert.IsTrue(ui.SelectPart(p.Kind, p)); Assert.IsFalse(ui.PopupOpen);
            Assert.IsFalse(ui.SelectPart(EquipmentKind.ShieldCore, p)); Assert.IsTrue(ui.SelectPart(p.Kind, null)); Assert.IsNull(ui.CurrentPart(p.Kind));
        }
        [Test] public void AllShippedContentCompiles()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<EquipmentCatalog>(StorageSceneBuilder.RootPath + "/Samples/Definitions/Catalog.asset");
            Assert.IsNotNull(catalog); Assert.AreEqual(16, catalog.Parts.Length); Assert.AreEqual(10, catalog.Recipes.Length);
            foreach (var recipe in catalog.Recipes) Assert.IsTrue(EquipmentCompiler.Compile(recipe.CreateLoadout()).Success, recipe.DisplayName);
            foreach (var part in catalog.Parts) Assert.IsTrue(EquipmentCompiler.Compile(catalog.Recipes[0].CreateLoadout().WithPart(part.Kind, part)).Success, part.DisplayName);
        }
        [Test] public void MissingSceneLinksRecoverWithoutRebuildingOrMovingPlayer()
        {
            var root = new GameObject("Storage"); garbage.Add(root);
            var controller = root.AddComponent<StorageController>(); var ui = root.AddComponent<EquipmentWorkbenchUI>(); root.AddComponent<ProjectilePool>();
            var player = new GameObject("Player"); player.transform.SetParent(root.transform); player.transform.position = new Vector3(2, 0, 3);
            var host = player.AddComponent<EquipmentHost>(); player.AddComponent<CharacterController>();
            var attacker = new GameObject("Attacker"); attacker.transform.SetParent(root.transform); attacker.AddComponent<LauncherHost>();
            var muzzle = new GameObject("Muzzle"); muzzle.transform.SetParent(attacker.transform);
            Assert.IsTrue(StorageBindingRepair.Repair(controller)); Assert.AreSame(host, controller.Player); Assert.AreSame(host, ui.Host);
            Assert.IsNotNull(ui.Catalog); Assert.IsNotNull(ui.InitialRecipe); Assert.AreEqual(8, controller.Threats.Length);
            Assert.IsTrue(controller.TryInitialize()); Assert.IsTrue(controller.Ready);
            Assert.AreEqual(new Vector3(2, 0, 3), player.transform.position);
            Assert.IsFalse(StorageBindingRepair.Repair(controller));
        }
        [Test] public void InitializationWithIncompleteHierarchyDoesNotThrow()
        {
            var root = new GameObject("IncompleteStorage"); garbage.Add(root); var controller = root.AddComponent<StorageController>();
            Assert.DoesNotThrow(() => Assert.IsFalse(controller.TryInitialize()));
        }
    }
}
