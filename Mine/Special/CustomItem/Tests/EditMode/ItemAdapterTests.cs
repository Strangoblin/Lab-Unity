using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CelToon.CustomItem.Adapters;
using CelToon.CustomItem.Contracts;
using CelToon.CustomItem.Runtime;
using CelToon.CustomEquipment.Compilation;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Definitions;
using CelToon.CustomEquipment.Runtime;
using CelToon.CustomLauncher.Compilation;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Definitions;
using CelToon.CustomLauncher.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CelToon.CustomItem.Tests.EditMode
{
    public sealed class ItemAdapterTests
    {
        private readonly List<UnityEngine.Object> garbage = new List<UnityEngine.Object>();
        private readonly List<string> temporaryFiles = new List<string>();
        private T Create<T>() where T : ScriptableObject
        { var value = ScriptableObject.CreateInstance<T>(); garbage.Add(value); return value; }
        [TearDown] public void Cleanup()
        {
            foreach (var value in garbage) if (value != null) UnityEngine.Object.DestroyImmediate(value);
            garbage.Clear();
            foreach (string path in temporaryFiles) if (File.Exists(path)) File.Delete(path);
            temporaryFiles.Clear();
        }
        private string SavePath()
        { string path = Path.Combine(Path.GetTempPath(), "CelToonItemAdapter-" + Guid.NewGuid().ToString("N") + ".json"); temporaryFiles.Add(path); return path; }

        private ItemSpecificationDefinition Core(string id, EquipmentKind kind = EquipmentKind.HealthCore,
            ItemRarity rarity = ItemRarity.Common, float amount = 40, params ItemAffixSnapshot[] affixes)
        {
            var s = Create<ItemSpecificationDefinition>();
            s.SpecificationId = id; s.PartId = "core." + kind; s.DisplayName = id;
            s.Family = ItemFamily.EquipmentCore; s.Rarity = rarity; s.Affixes = affixes;
            var operation = kind == EquipmentKind.HealthCore ? EquipmentOperation.HealthAdd
                : kind == EquipmentKind.ShieldCore ? EquipmentOperation.ShieldAdd
                : kind == EquipmentKind.StaminaCore ? EquipmentOperation.StaminaAdd : EquipmentOperation.EnergyAdd;
            s.Equipment = new EquipmentItemPayload { Kind = kind,
                PrimaryOperations = new[] { new EquipmentStep(operation, amount) },
                Affixes = affixes.SelectMany(ItemAffixRules.ExpandEquipment).ToArray() };
            return s;
        }
        private ItemSpecificationDefinition Round(string id, float damage = 10)
        {
            var s = Create<ItemSpecificationDefinition>();
            s.SpecificationId = id; s.PartId = "round.standard"; s.DisplayName = id;
            s.Family = ItemFamily.LauncherPart; s.Rarity = ItemRarity.Common;
            s.Launcher = new LauncherItemPayload { Kind = PartKind.Projectile, Damage = damage, Speed = 30,
                Radius = .08f, Lifetime = 3, Color = Color.cyan, EnergyPerProjectile = 1 };
            return s;
        }
        private ItemSpecificationCatalog Catalog(params ItemSpecificationDefinition[] specs)
        { var c = Create<ItemSpecificationCatalog>(); c.Specifications = specs; return c; }
        private ItemEquipmentService Service(ItemSpecificationCatalog catalog, out LauncherHost launcher, out EquipmentHost equipment)
        {
            var go = new GameObject("ItemAdapterHosts"); garbage.Add(go);
            launcher = go.AddComponent<LauncherHost>(); equipment = go.AddComponent<EquipmentHost>(); equipment.AutoTick = false;
            var l = Create<LauncherRecipeDefinition>(); var e = Create<EquipmentRecipeDefinition>();
            Assert.IsTrue(launcher.TryEquip(l.CreateLoadout())); Assert.IsTrue(equipment.TryEquip(e.CreateLoadout()));
            return new ItemEquipmentService(new ItemStore(), catalog, launcher, equipment, l, e);
        }
        private static void Granted(ItemEquipmentService service, params ItemInstance[] items)
        {
            var result = service.Store.Grant(Guid.NewGuid().ToString("N"), items, ItemEquipmentService.BackpackId);
            Assert.IsTrue(result.Success, result.Message);
        }
        private static void Succeeded(ItemOperationResult result) => Assert.IsTrue(result.Success, result.Code + ": " + result.Message);
        private sealed class Emitter : IProjectileEmitter
        { public bool TryEmit(IReadOnlyList<ProjectileCommand> commands) => true; }

        [Test] public void WhiteShieldHasOneSemanticAffixAndTwoExecutableOperations()
        {
            var regen = ItemAffixRules.Equipment("equipment.shield.regen", "standard", 8, 3); regen.Role = ItemAffixRole.Baseline;
            var spec = Core("shield.white", EquipmentKind.ShieldCore, ItemRarity.Common, 50, regen);
            var factory = new ItemFactory(Catalog(spec)); var item = factory.Create(spec.SpecificationId);
            Assert.IsTrue(factory.Validate(item, out string problem), problem);
            Assert.AreEqual(1, item.Content.Affixes.Length);
            var resolved = ItemFactory.ResolveEquipment(item); Assert.AreEqual(2, resolved.Affixes.Count);
            var compiled = EquipmentCompiler.Compile(Create<EquipmentRecipeDefinition>(), new[] { resolved });
            Assert.IsTrue(compiled.Success); Assert.AreEqual(50, compiled.Plan.Stats.MaxShield);
            Assert.AreEqual(8, compiled.Plan.Stats.ShieldRegen); Assert.AreEqual(3, compiled.Plan.Stats.ShieldDelay);
        }

        [Test] public void ThirdSemanticFunctionCannotHideBehindCombinedPresentation()
        {
            var armor = ItemAffixRules.Equipment("equipment.armor", "standard", 3);
            var burn = ItemAffixRules.Equipment("equipment.resistance.burn", "standard", .2f);
            var mark = ItemAffixRules.Equipment("equipment.resistance.mark", "standard", .2f);
            var three = Core("health.three", EquipmentKind.HealthCore, ItemRarity.Rare, 40, armor, burn, mark);
            Assert.Throws<ArgumentException>(() => new ItemFactory(Catalog(three)));
            var hidden = Core("health.hidden", EquipmentKind.HealthCore, ItemRarity.Uncommon, 40, armor);
            hidden.Affixes[0].SemanticGroupIds = new[] { armor.AffixId, burn.AffixId, mark.AffixId };
            hidden.Equipment.Affixes = new[] { new EquipmentStep(EquipmentOperation.ArmorAdd, 3),
                new EquipmentStep(EquipmentOperation.BurnResistance, .2f), new EquipmentStep(EquipmentOperation.MarkResistance, .2f) };
            Assert.Throws<ArgumentException>(() => new ItemFactory(Catalog(hidden)));
        }

        [Test] public void UnimplementedLegendarySignaturesAndCrossCategoryLauncherEffectsAreRejected()
        {
            var legendary = Round("round.legendary"); legendary.Rarity = ItemRarity.Legendary;
            Assert.Throws<ArgumentException>(() => new ItemFactory(Catalog(legendary)));
            var wrong = Round("damage.wrong"); wrong.Launcher.Kind = PartKind.Damage;
            wrong.Launcher.Operations = new[] { new EffectStep(EffectOperation.Homing, 3, 25) };
            Assert.Throws<ArgumentException>(() => new ItemFactory(Catalog(wrong)));
        }

        [Test] public void RepeatedTemplateCreationProducesDistinctItemsAndDetachedFactorySnapshots()
        {
            var armor = ItemAffixRules.Equipment("equipment.armor", "standard", 3);
            var burn = ItemAffixRules.Equipment("equipment.resistance.burn", "standard", .2f);
            var spec = Core("health.rare", EquipmentKind.HealthCore, ItemRarity.Rare, 40, armor, burn);
            var factory = new ItemFactory(Catalog(spec)); var first = factory.Create(spec.SpecificationId);
            spec.Equipment.PrimaryOperations[0].A = 900; spec.Affixes[0].Parameters[0] = 90;
            spec.Equipment.Affixes[0].A = 90; spec.DisplayName = "Changed shared template";
            var second = factory.Create(spec.SpecificationId);
            Assert.AreNotEqual(first.InstanceId, second.InstanceId); Assert.AreEqual(first.DefinitionRef.PartId, second.DefinitionRef.PartId);
            Assert.AreEqual(first.Content.PayloadJson, second.Content.PayloadJson); Assert.AreEqual("health.rare", second.DisplayName);
            first.Content.Affixes[0].Parameters[0] = 99;
            Assert.AreEqual(3, second.Content.Affixes[0].Parameters[0]);
            Assert.AreEqual(40, ItemFactory.ResolveEquipment(second).PrimaryOperations[0].A);
            Assert.IsTrue(factory.Validate(second, out string problem), problem);
        }

        [Test] public void TamperedFixedContentSurvivesCarrierSaveButCannotEquip()
        {
            var spec = Core("health.white"); var catalog = Catalog(spec); var factory = new ItemFactory(catalog);
            var item = factory.Create(spec.SpecificationId);
            var payload = JsonUtility.FromJson<EquipmentItemPayload>(item.Content.PayloadJson); payload.PrimaryOperations[0].A = 80;
            item.Content.PayloadJson = JsonUtility.ToJson(payload);
            var service = Service(catalog, out var launcher, out var equipment); Granted(service, item);
            var roundTrip = ItemSaveService.FromJson(ItemSaveService.ToJson(service.Store));
            Assert.AreEqual(item.Content.PayloadJson, roundTrip.GetItem(item.InstanceId).Content.PayloadJson);
            Assert.IsFalse(factory.Validate(roundTrip.GetItem(item.InstanceId), out _));
            Succeeded(service.EnterItemMode()); var before = equipment.State.Plan;
            var result = service.Equip(item.InstanceId); Assert.IsFalse(result.Success);
            Assert.AreEqual(ItemEquipmentService.BackpackId, service.Store.GetLocation(item.InstanceId).ContainerId);
            Assert.AreSame(before, equipment.State.Plan); Assert.AreEqual(100, equipment.State.Health);
        }

        [Test] public void ActiveEquipAndUnequipPreserveResourcesStatusesAndFiringCooldown()
        {
            var catalog = Catalog(Core("health.small"), Core("health.large", amount: 80), Round("round.small"), Round("round.large", 20));
            var factory = new ItemFactory(catalog); var service = Service(catalog, out var launcher, out var equipment);
            var small = factory.Create("health.small"); var large = factory.Create("health.large");
            var round = factory.Create("round.small"); var stronger = factory.Create("round.large");
            Granted(service, small, large, round, stronger); Succeeded(service.EnterItemMode());
            Succeeded(service.Equip(small.InstanceId)); Succeeded(service.Equip(round.InstanceId));
            Assert.AreEqual(FireResult.Fired, launcher.TryFire(new Emitter(), new FireAim(Vector3.zero, Vector3.forward, Vector3.up), 12));
            equipment.State.ApplyDamage(20); Assert.IsTrue(equipment.State.TrySpendEnergy(25));
            equipment.State.ApplyStatus(new StatusInfo(StatusKind.Poison, 2, 3, 0, 0));
            var ls = launcher.State; var es = equipment.State;
            float health = es.Health, energy = es.Energy, battery = ls.Energy, cooldown = ls.NextFireTime;
            int hits = es.Hits, shots = ls.ShotCount;
            Succeeded(service.Equip(large.InstanceId)); Succeeded(service.Equip(stronger.InstanceId));
            Succeeded(service.Unequip(ItemFamily.EquipmentCore, "healthcore.main"));
            Succeeded(service.Unequip(ItemFamily.LauncherPart, "projectile.main"));
            Assert.AreSame(es, equipment.State); Assert.AreSame(ls, launcher.State);
            Assert.AreEqual(health, es.Health); Assert.AreEqual(energy, es.Energy); Assert.AreEqual(1, es.StatusCount); Assert.AreEqual(hits, es.Hits);
            Assert.AreEqual(battery, ls.Energy); Assert.AreEqual(cooldown, ls.NextFireTime); Assert.AreEqual(shots, ls.ShotCount);
            Assert.IsNull(launcher.Loadout); Assert.IsNull(equipment.Loadout);
            Succeeded(service.ExitItemMode()); Assert.IsNotNull(launcher.Loadout); Assert.IsNotNull(equipment.Loadout);
            Assert.AreEqual(health, es.Health); Assert.AreEqual(energy, es.Energy); Assert.AreEqual(cooldown, ls.NextFireTime);
        }

        [Test] public void FullBackpackSwapUsesIncomingPositionAndFailedUnequipKeepsPlan()
        {
            var catalog = Catalog(Core("health.white")); var factory = new ItemFactory(catalog);
            var service = Service(catalog, out var launcher, out var equipment); Succeeded(service.EnterItemMode());
            var old = factory.Create("health.white"); Granted(service, old); Succeeded(service.Equip(old.InstanceId));
            var bag = Enumerable.Range(0, 16).Select(_ => factory.Create("health.white")).ToArray(); Granted(service, bag);
            string freed = service.Store.GetLocation(bag[0].InstanceId).PositionKey;
            Succeeded(service.Equip(bag[0].InstanceId)); Assert.AreEqual(16, service.Store.CountIn(ItemEquipmentService.BackpackId));
            Assert.AreEqual(freed, service.Store.GetLocation(old.InstanceId).PositionKey);
            Assert.AreEqual(ItemEquipmentService.BackpackId, service.Store.GetLocation(old.InstanceId).ContainerId);
            var before = equipment.State.Plan; var result = service.Unequip(ItemFamily.EquipmentCore, "healthcore.main");
            Assert.IsFalse(result.Success); Assert.AreEqual("ContainerFull", result.Code);
            Assert.AreSame(before, equipment.State.Plan); Assert.AreEqual(ItemEquipmentService.EquipmentId, service.Store.GetLocation(bag[0].InstanceId).ContainerId);
        }

        [Test] public void ActiveLoadReinstatesPositionsWithoutRefillingResourcesOrClearingCooldown()
        {
            var catalog = Catalog(Core("health.white"), Round("round.white")); var factory = new ItemFactory(catalog);
            var service = Service(catalog, out var launcher, out var equipment); var health = factory.Create("health.white"); var round = factory.Create("round.white");
            Granted(service, health, round); Succeeded(service.EnterItemMode()); Succeeded(service.Equip(health.InstanceId)); Succeeded(service.Equip(round.InstanceId));
            string path = SavePath(); Succeeded(service.Save(path));
            Assert.AreEqual(FireResult.Fired, launcher.TryFire(new Emitter(), new FireAim(Vector3.zero, Vector3.forward, Vector3.up), 20));
            equipment.State.ApplyDamage(15); equipment.State.TrySpendEnergy(20);
            float remainingHealth = equipment.State.Health, remainingEnergy = equipment.State.Energy, battery = launcher.State.Energy, cooldown = launcher.State.NextFireTime;
            var state = equipment.State; var firing = launcher.State;
            Succeeded(service.Unequip(ItemFamily.EquipmentCore, "healthcore.main")); Succeeded(service.Unequip(ItemFamily.LauncherPart, "projectile.main"));
            Succeeded(service.Load(path));
            Assert.AreSame(state, equipment.State); Assert.AreSame(firing, launcher.State);
            Assert.AreEqual(ItemEquipmentService.EquipmentId, service.Store.GetLocation(health.InstanceId).ContainerId);
            Assert.AreEqual("healthcore.main", service.Store.GetLocation(health.InstanceId).PositionKey);
            Assert.AreEqual(ItemEquipmentService.LauncherId, service.Store.GetLocation(round.InstanceId).ContainerId);
            Assert.AreEqual(remainingHealth, state.Health); Assert.AreEqual(remainingEnergy, state.Energy);
            Assert.AreEqual(battery, firing.Energy); Assert.AreEqual(cooldown, firing.NextFireTime);
            Assert.AreEqual(10, firing.Plan.Projectile.Damage); Assert.AreEqual(140, state.Plan.Stats.MaxHealth);
        }

        [Test] public void UnsupportedEquippedPayloadIsRetainedAndItemModeEntryFailsWithoutThrowing()
        {
            var spec = Core("health.white"); var catalog = Catalog(spec); var factory = new ItemFactory(catalog);
            var service = Service(catalog, out var launcher, out var equipment); var item = factory.Create(spec.SpecificationId);
            item.Content.PayloadJson = "null"; Granted(service, item);
            Assert.IsTrue(service.Store.TrySwap("legacy-import", item.InstanceId, ItemEquipmentService.EquipmentId, "healthcore.main", _ => null).Success);
            var lp = launcher.State.Plan; var ep = equipment.State.Plan;
            Assert.DoesNotThrow(() => Assert.IsFalse(service.EnterItemMode().Success)); Assert.IsFalse(service.Active);
            Assert.AreSame(lp, launcher.State.Plan); Assert.AreSame(ep, equipment.State.Plan);
            Assert.AreEqual("null", service.Store.GetItem(item.InstanceId).Content.PayloadJson);
        }

        [Test] public void LoadingIncompleteContainerSetFailsBeforeReplacingActiveStore()
        {
            var catalog = Catalog(Core("health.white")); var service = Service(catalog, out var launcher, out var equipment);
            Succeeded(service.EnterItemMode()); var originalStore = service.Store; var lp = launcher.State.Plan; var ep = equipment.State.Plan;
            string path = SavePath(); ItemSaveService.Save(path, new ItemStore());
            Assert.IsFalse(service.Load(path).Success); Assert.AreSame(originalStore, service.Store);
            Assert.AreSame(lp, launcher.State.Plan); Assert.AreSame(ep, equipment.State.Plan);
        }

        [Test] public void ShippedSpecificationCatalogCreatesValidatedExecutableItems()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemSpecificationCatalog>("Assets/Mine/Special/CustomItem/Samples/Definitions/Catalog.asset");
            Assert.IsNotNull(catalog); Assert.That(catalog.Specifications.Length, Is.GreaterThanOrEqualTo(8));
            var factory = new ItemFactory(catalog); var l = Create<LauncherRecipeDefinition>(); var e = Create<EquipmentRecipeDefinition>();
            foreach (var spec in catalog.Specifications.Where(s => s != null && s.Production))
            {
                var item = factory.Create(spec.SpecificationId); Assert.IsTrue(factory.Validate(item, out string problem), spec.DisplayName + ": " + problem);
                if (spec.Family == ItemFamily.EquipmentCore) Assert.IsTrue(EquipmentCompiler.Compile(e, new[] { ItemFactory.ResolveEquipment(item) }).CanCommit, spec.DisplayName);
                else Assert.IsTrue(LauncherCompiler.Compile(l, new[] { ItemFactory.ResolveLauncher(item) }).CanCommit, spec.DisplayName);
            }
        }
    }
}
