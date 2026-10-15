using System;
using System.IO;
using CelToon.CustomItem.Contracts;
using CelToon.CustomItem.Runtime;
using NUnit.Framework;

namespace CelToon.CustomItem.Tests.EditMode
{
    public class ItemStoreTests
    {
        static ItemStore Store(int capacity = 2)
        {
            var store = new ItemStore();
            Assert.That(store.RegisterContainer(new ItemContainerDefinition { ContainerId = "bag", OwnerId = "player", Kind = ItemContainerKind.Backpack, Capacity = capacity }).Success, Is.True);
            Assert.That(store.RegisterContainer(new ItemContainerDefinition { ContainerId = "equipment", OwnerId = "player", Kind = ItemContainerKind.Equipment, Capacity = 4 }).Success, Is.True);
            Assert.That(store.RegisterContainer(new ItemContainerDefinition { ContainerId = "warehouse", OwnerId = "player", Kind = ItemContainerKind.Warehouse, Capacity = 10 }).Success, Is.True);
            Assert.That(store.RegisterContainer(new ItemContainerDefinition { ContainerId = "world", OwnerId = "player", Kind = ItemContainerKind.World, Capacity = 10 }).Success, Is.True);
            return store;
        }

        static ItemInstance Item(string id) => new ItemInstance {
            InstanceId = id, DefinitionRef = new ItemDefinitionRef { Family = ItemFamily.EquipmentCore, PartId = "shield.standard" },
            Rarity = ItemRarity.Uncommon, DisplayName = "Shield", Description = "Fixed content",
            Content = new ItemContentSnapshot { PayloadFormat = "equipment-core.v1", PayloadJson = "{\"shield\":50}",
                SpecificationId = "shield.standard.green", Affixes = new[] { new ItemAffixSnapshot {
                    AffixId = "shield.regen", ParameterPresetId = "standard", Parameters = new[] { 8f, 3f },
                    Role = ItemAffixRole.Ordinary, SemanticGroupIds = new[] { "shield.regen" }, UniqueGroupIds = new[] { "shield.regen" } } } }
        };

        [Test]
        public void RepeatRewardReturnsOriginalIdentityEvenWhenCallerRecreatesItems()
        {
            var store = Store();
            var first = store.Grant("quest.reward", new[] { Item("first") }, "bag");
            var retry = store.Grant("quest.reward", new[] { Item("unused-new-id") }, "bag");
            Assert.That(first.Success && retry.Success, Is.True);
            Assert.That(retry.InstanceIds, Is.EqualTo(new[] { "first" }));
            Assert.That(store.CountIn("bag"), Is.EqualTo(1));
            Assert.That(store.GetItem("unused-new-id"), Is.Null);
            retry.InstanceIds[0] = "mutated";
            Assert.That(store.GetReceipt("quest.reward").InstanceIds[0], Is.EqualTo("first"));
            Assert.That(store.Move("quest.reward", "first", "warehouse").Code, Is.EqualTo("RequestConflict"));
        }

        [Test]
        public void GrantAndReadsNeverShareMutableContent()
        {
            var store = Store();
            var original = Item("first");
            Assert.That(store.Grant("reward", new[] { original }, "bag").Success, Is.True);
            original.Content.Affixes[0].Parameters[0] = 1000f;
            original.DefinitionRef.PartId = "edited";
            store.GetItem("first").Content.Affixes[0].Parameters[0] = 2000f;
            store.ItemsIn("bag")[0].Content.Affixes[0].SemanticGroupIds[0] = "edited";
            store.GetLocation("first").ContainerId = "edited";
            store.GetContainer("bag").Capacity = 0;
            var saved = store.GetItem("first");
            Assert.That(saved.Content.Affixes[0].Parameters[0], Is.EqualTo(8f));
            Assert.That(saved.Content.Affixes[0].SemanticGroupIds[0], Is.EqualTo("shield.regen"));
            Assert.That(saved.DefinitionRef.PartId, Is.EqualTo("shield.standard"));
            Assert.That(store.GetLocation("first").ContainerId, Is.EqualTo("bag"));
            Assert.That(store.GetContainer("bag").Capacity, Is.EqualTo(2));
        }

        [Test]
        public void BatchGrantIsAtomicAndFailedRequestCanBeRetried()
        {
            var store = Store(1);
            Assert.That(store.Grant("reward", new[] { Item("first"), Item("second") }, "bag").Success, Is.False);
            Assert.That(store.CountIn("bag"), Is.Zero);
            Assert.That(store.GetReceipt("reward"), Is.Null);
            Assert.That(store.Grant("reward", new[] { Item("first") }, "bag").Success, Is.True);
            var invalid = Item("invalid");
            invalid.Content.Affixes[0].Parameters[0] = float.NaN;
            Assert.That(store.Grant("invalid", new[] { invalid }, "warehouse").Code, Is.EqualTo("InvalidItem"));
            Assert.That(store.GetItem("invalid"), Is.Null);
        }

        [Test]
        public void SameDefinitionCanHaveDistinctInstancesAndTransfersKeepTheirContent()
        {
            var store = Store();
            Assert.That(store.Grant("reward", new[] { Item("first"), Item("second") }, "bag").Success, Is.True);
            Assert.That(store.Move("transfer", "first", "warehouse").Success, Is.True);
            Assert.That(store.GetLocation("first").ContainerId, Is.EqualTo("warehouse"));
            Assert.That(store.GetLocation("second").ContainerId, Is.EqualTo("bag"));
            Assert.That(store.GetItem("first").Content.PayloadJson, Is.EqualTo(store.GetItem("second").Content.PayloadJson));
            Assert.That(store.ItemsIn("warehouse")[0].InstanceId, Is.EqualTo("first"));
            Assert.That(store.Move("equip-bypass", "first", "equipment", "shieldcore.main").Code, Is.EqualTo("RequiresEquipmentCommand"));
        }

        [Test]
        public void FullBackpackCanSwapButCannotPureUnequip()
        {
            var store = Store(1);
            Assert.That(store.Grant("old", new[] { Item("old") }, "bag").Success, Is.True);
            Assert.That(store.TrySwap("equip-old", "old", "equipment", "shieldcore.main", _ => null).Success, Is.True);
            Assert.That(store.Grant("new", new[] { Item("new") }, "bag").Success, Is.True);
            string freedPosition = store.GetLocation("new").PositionKey;
            Assert.That(store.TrySwap("equip-new", "new", "equipment", "shieldcore.main", candidate => candidate.Length == 1 && candidate[0].InstanceId == "new" ? null : "wrong candidate").Success, Is.True);
            Assert.That(store.GetLocation("old").ContainerId, Is.EqualTo("bag"));
            Assert.That(store.GetLocation("old").PositionKey, Is.EqualTo(freedPosition));
            Assert.That(store.Unequip("unequip", "equipment", "shieldcore.main", "bag", _ => null).Code, Is.EqualTo("ContainerFull"));
            Assert.That(store.GetLocation("new").ContainerId, Is.EqualTo("equipment"));
            Assert.That(store.GetReceipt("unequip"), Is.Null);
            Assert.That(store.Move("store-old", "old", "warehouse").Success, Is.True);
            Assert.That(store.Unequip("unequip", "equipment", "shieldcore.main", "bag", candidate => candidate.Length == 0 ? null : "wrong candidate").Success, Is.True);
        }

        [Test]
        public void CandidateFailureAndExceptionLeaveAllLocationsAndContentUnchanged()
        {
            var store = Store();
            Assert.That(store.Grant("reward", new[] { Item("old"), Item("new") }, "bag").Success, Is.True);
            Assert.That(store.TrySwap("equip-old", "old", "equipment", "shieldcore.main", _ => null).Success, Is.True);
            string before = ItemSaveService.ToJson(store);
            var rejected = store.TrySwap("equip-new", "new", "equipment", "shieldcore.main", candidate => {
                candidate[0].Content.Affixes[0].Parameters[0] = 999f;
                return "semantic conflict";
            });
            Assert.That(rejected.Code, Is.EqualTo("CandidateRejected"));
            Assert.That(ItemSaveService.ToJson(store), Is.EqualTo(before));
            Assert.That(store.TrySwap("equip-new", "new", "equipment", "shieldcore.main", _ => throw new InvalidOperationException("compiler failed")).Code, Is.EqualTo("CandidateValidationFailed"));
            Assert.That(ItemSaveService.ToJson(store), Is.EqualTo(before));
        }

        [Test]
        public void CandidateValidatorCannotMutateStoreReentrantly()
        {
            var store = Store();
            Assert.That(store.Grant("reward", new[] { Item("first") }, "bag").Success, Is.True);
            Assert.That(store.TrySwap("equip", "first", "equipment", "shieldcore.main", _ => {
                Assert.That(store.Move("reentrant", "first", "warehouse").Code, Is.EqualTo("Busy"));
                Assert.That(store.Grant("reentrant-reward", new[] { Item("second") }, "bag").Code, Is.EqualTo("Busy"));
                return null;
            }).Success, Is.True);
            Assert.That(store.CountIn("warehouse"), Is.Zero);
            Assert.That(store.GetItem("second"), Is.Null);
        }

        [Test]
        public void WorldPickupPreservesIdentityAndFullBackpackLeavesWorldItem()
        {
            var store = Store(1);
            Assert.That(store.Grant("world-reward", new[] { Item("world-item") }, "world").Success, Is.True);
            Assert.That(store.Grant("bag-reward", new[] { Item("bag-item") }, "bag").Success, Is.True);
            Assert.That(store.Move("pickup", "world-item", "bag").Code, Is.EqualTo("ContainerFull"));
            Assert.That(store.GetLocation("world-item").ContainerId, Is.EqualTo("world"));
            Assert.That(store.Move("stash", "bag-item", "warehouse").Success, Is.True);
            Assert.That(store.Move("pickup", "world-item", "bag").Success, Is.True);
            Assert.That(store.GetLocation("world-item").ContainerId, Is.EqualTo("bag"));
            Assert.That(store.GetItem("world-item").InstanceId, Is.EqualTo("world-item"));
        }

        [Test]
        public void OwnerAndExclusivePositionRulesRejectTransfers()
        {
            var store = Store();
            Assert.That(store.RegisterContainer(new ItemContainerDefinition { ContainerId = "other", OwnerId = "other-player", Kind = ItemContainerKind.Backpack, Capacity = 2 }).Success, Is.True);
            Assert.That(store.Grant("reward", new[] { Item("first"), Item("second") }, "bag").Success, Is.True);
            Assert.That(store.Move("foreign", "first", "other").Code, Is.EqualTo("WrongOwner"));
            Assert.That(store.Move("occupied", "first", "bag", store.GetLocation("second").PositionKey).Code, Is.EqualTo("PositionOccupied"));
            Assert.That(store.CountIn("bag"), Is.EqualTo(2));
        }

        [Test]
        public void SaveLoadPreservesUnknownPayloadLocationsAndDurableReceipts()
        {
            var store = Store();
            var item = Item("future");
            item.Content.PayloadFormat = "future-format";
            item.Content.PayloadVersion = 999;
            item.Content.PayloadJson = "{\"unknown\":{\"ordered\":[3,1,2]}}";
            Assert.That(store.Grant("reward", new[] { item }, "bag").Success, Is.True);
            Assert.That(store.TrySwap("equip", "future", "equipment", "shieldcore.main", _ => null).Success, Is.True);
            var loaded = ItemSaveService.FromJson(ItemSaveService.ToJson(store));
            Assert.That(ItemSaveService.ToJson(loaded), Is.EqualTo(ItemSaveService.ToJson(store)));
            Assert.That(loaded.GetItem("future").Content.PayloadJson, Is.EqualTo(item.Content.PayloadJson));
            Assert.That(loaded.GetLocation("future").ContainerId, Is.EqualTo("equipment"));
            var regenerated = item.Clone();
            regenerated.InstanceId = "unused";
            Assert.That(loaded.Grant("reward", new[] { regenerated }, "bag").InstanceIds, Is.EqualTo(new[] { "future" }));
            Assert.That(loaded.GetItem("unused"), Is.Null);
            Assert.That(loaded.TrySwap("equip", "future", "equipment", "shieldcore.main", _ => "must not revalidate receipt").Success, Is.True);
        }

        [Test]
        public void FileSaveReplacesCompleteOldSaveAndLeavesNoTemporaryFiles()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CelToonItemTests-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "items.json");
            try
            {
                var store = Store();
                Assert.That(store.Grant("first", new[] { Item("first") }, "bag").Success, Is.True);
                ItemSaveService.Save(path, store);
                Assert.That(store.Grant("second", new[] { Item("second") }, "bag").Success, Is.True);
                ItemSaveService.Save(path, store);
                Assert.That(ItemSaveService.Load(path).CountIn("bag"), Is.EqualTo(2));
                Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void MalformedImportRejectsMissingDuplicateAndOverCapacityLocations()
        {
            var store = Store();
            Assert.That(store.Grant("reward", new[] { Item("first"), Item("second") }, "bag").Success, Is.True);
            var missing = store.ExportSnapshot();
            missing.Locations = new[] { missing.Locations[0] };
            Assert.Throws<InvalidDataException>(() => ItemStore.FromSnapshot(missing));
            var duplicate = store.ExportSnapshot();
            duplicate.Locations[1].Location.PositionKey = duplicate.Locations[0].Location.PositionKey;
            Assert.Throws<InvalidDataException>(() => ItemStore.FromSnapshot(duplicate));
            var overCapacity = store.ExportSnapshot();
            foreach (var container in overCapacity.Containers) if (container.ContainerId == "bag") container.Capacity = 1;
            Assert.Throws<InvalidDataException>(() => ItemStore.FromSnapshot(overCapacity));
            var duplicateItem = store.ExportSnapshot();
            duplicateItem.Items[1].InstanceId = duplicateItem.Items[0].InstanceId;
            Assert.Throws<InvalidDataException>(() => ItemStore.FromSnapshot(duplicateItem));
            var badReceipt = store.ExportSnapshot();
            badReceipt.Receipts[0].Result.Success = false;
            Assert.Throws<InvalidDataException>(() => ItemStore.FromSnapshot(badReceipt));
            Assert.Throws<InvalidDataException>(() => ItemSaveService.FromJson("not JSON"));
            Assert.Throws<InvalidDataException>(() => ItemSaveService.FromJson("{}"));
        }

        [Test]
        public void MalformedImportRejectsEmptyAndDuplicateReceiptIdentities()
        {
            var store = Store();
            Assert.That(store.Grant("reward", new[] { Item("first"), Item("second") }, "bag").Success, Is.True);
            var empty = store.ExportSnapshot();
            empty.Receipts[0].Result.InstanceIds = Array.Empty<string>();
            Assert.Throws<InvalidDataException>(() => ItemStore.FromSnapshot(empty));
            var duplicate = store.ExportSnapshot();
            duplicate.Receipts[0].Result.InstanceIds = new[] { "first", "first" };
            Assert.Throws<InvalidDataException>(() => ItemStore.FromSnapshot(duplicate));
            var move = store.ExportSnapshot();
            move.Receipts[0].Operation = "Move";
            Assert.Throws<InvalidDataException>(() => ItemStore.FromSnapshot(move));
        }
    }
}
