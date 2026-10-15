using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CelToon.CustomItem.Contracts;
using UnityEngine;

namespace CelToon.CustomItem.Runtime
{
    /// <summary>
    /// Main-thread item ledger. Locations are authoritative; every read is a defensive copy.
    /// Equipment commands validate the final candidate before changing any position.
    /// </summary>
    public sealed class ItemStore
    {
        readonly Dictionary<string, ItemInstance> items = new Dictionary<string, ItemInstance>(StringComparer.Ordinal);
        readonly Dictionary<string, ItemLocation> locations = new Dictionary<string, ItemLocation>(StringComparer.Ordinal);
        readonly Dictionary<string, ItemContainerDefinition> containers = new Dictionary<string, ItemContainerDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, ItemRequestReceipt> receipts = new Dictionary<string, ItemRequestReceipt>(StringComparer.Ordinal);
        bool validatingCandidate;

        [Serializable]
        sealed class CommandArguments
        {
            public string ContainerId;
            public string InstanceId;
            public string PositionKey;
            public ItemInstance[] Items;
        }

        public ItemOperationResult RegisterContainer(ItemContainerDefinition definition)
        {
            if (validatingCandidate) return Failure("Busy", "A candidate is being validated.");
            string error = ItemDataValidation.ValidateContainer(definition);
            if (error != null) return Failure("InvalidContainer", error);
            if (containers.TryGetValue(definition.ContainerId, out var existing))
                return existing.OwnerId == definition.OwnerId && existing.Kind == definition.Kind && existing.Capacity == definition.Capacity
                    ? Success("ContainerRegistered", Array.Empty<string>()) : Failure("ContainerExists", "The container is already registered with different settings.");
            containers.Add(definition.ContainerId, definition.Clone());
            return Success("ContainerRegistered", Array.Empty<string>());
        }

        public ItemContainerDefinition GetContainer(string containerId) => containerId != null && containers.TryGetValue(containerId, out var container) ? container.Clone() : null;
        public ItemContainerDefinition[] Containers() => containers.Values.OrderBy(c => c.ContainerId, StringComparer.Ordinal).Select(c => c.Clone()).ToArray();
        public ItemInstance GetItem(string instanceId) => instanceId != null && items.TryGetValue(instanceId, out var item) ? item.Clone() : null;
        public ItemLocation GetLocation(string instanceId) => instanceId != null && locations.TryGetValue(instanceId, out var location) ? location.Clone() : null;
        public ItemOperationResult GetReceipt(string requestId) => requestId != null && receipts.TryGetValue(requestId, out var receipt) ? receipt.Result.Clone() : null;
        public ItemInstance[] ItemsIn(string containerId) => OrderedIdsIn(containerId).Select(id => items[id].Clone()).ToArray();
        public int CountIn(string containerId) => locations.Values.Count(location => location.ContainerId == containerId);

        IEnumerable<string> OrderedIdsIn(string containerId) => locations.Where(pair => pair.Value.ContainerId == containerId)
            .OrderBy(pair => pair.Value.PositionKey, StringComparer.Ordinal).ThenBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key);

        public ItemOperationResult Grant(string requestId, ItemInstance[] grantedItems, string containerId)
        {
            ItemInstance[] fingerprintItems = grantedItems == null ? null : grantedItems.Select(item => item?.Clone()).ToArray();
            if (fingerprintItems != null) foreach (var item in fingerprintItems) if (item != null) item.InstanceId = null;
            string key = ArgumentKey(new CommandArguments { ContainerId = containerId, Items = fingerprintItems });
            var prior = CheckRequest(requestId, "Grant", key);
            if (prior != null) return prior;
            if (!TryContainer(containerId, out var container)) return Failure("UnknownContainer", "The destination container does not exist.");
            if (IsEquipped(container)) return Failure("RequiresEquipmentCommand", "Grant into a holding container, then equip through the adapter.");
            if (grantedItems == null || grantedItems.Length == 0) return Failure("InvalidItems", "At least one item is required.");
            if ((long)CountIn(containerId) + grantedItems.Length > container.Capacity) return Failure("ContainerFull", "The destination container is full.");
            var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in grantedItems)
            {
                string error = ItemDataValidation.ValidateInstance(item);
                if (error != null) return Failure("InvalidItem", error);
                if (!uniqueIds.Add(item.InstanceId) || items.ContainsKey(item.InstanceId)) return Failure("DuplicateInstance", "An instance ID already exists.");
            }
            var occupied = OccupiedPositions(containerId);
            var positions = new string[grantedItems.Length];
            for (int i = 0; i < positions.Length; i++) positions[i] = ReservePosition(occupied);
            for (int i = 0; i < grantedItems.Length; i++)
            {
                var item = grantedItems[i];
                items.Add(item.InstanceId, item.Clone());
                locations.Add(item.InstanceId, new ItemLocation { ContainerId = containerId, PositionKey = positions[i] });
            }
            return Record(requestId, "Grant", key, Success("Granted", grantedItems.Select(item => item.InstanceId).ToArray()));
        }

        public ItemOperationResult Move(string requestId, string instanceId, string destinationContainerId, string positionKey = null)
        {
            string key = ArgumentKey(new CommandArguments { ContainerId = destinationContainerId, InstanceId = instanceId, PositionKey = positionKey });
            var prior = CheckRequest(requestId, "Move", key);
            if (prior != null) return prior;
            if (!TryLocation(instanceId, out var source)) return Failure("UnknownItem", "The item does not exist.");
            if (!TryContainer(destinationContainerId, out var destination)) return Failure("UnknownContainer", "The destination container does not exist.");
            var sourceContainer = containers[source.ContainerId];
            if (sourceContainer.OwnerId != destination.OwnerId) return Failure("WrongOwner", "Items cannot cross owner boundaries.");
            if (IsEquipped(sourceContainer) || IsEquipped(destination)) return Failure("RequiresEquipmentCommand", "Use the equipment adapter to change an equipped location.");
            bool sameContainer = source.ContainerId == destinationContainerId;
            if (!sameContainer && CountIn(destinationContainerId) >= destination.Capacity) return Failure("ContainerFull", "The destination container is full.");
            string resolvedPosition = positionKey;
            if (resolvedPosition == null) resolvedPosition = sameContainer ? source.PositionKey : ReservePosition(OccupiedPositions(destinationContainerId));
            if (!ItemDataValidation.HasId(resolvedPosition)) return Failure("InvalidPosition", "A position key cannot be empty.");
            if (FindAt(destinationContainerId, resolvedPosition, instanceId) != null) return Failure("PositionOccupied", "The destination position is occupied.");
            locations[instanceId] = new ItemLocation { ContainerId = destinationContainerId, PositionKey = resolvedPosition };
            return Record(requestId, "Move", key, Success("Moved", new[] { instanceId }));
        }

        public ItemOperationResult TrySwap(string requestId, string incomingId, string equippedContainerId, string slotKey, Func<ItemInstance[], string> validateCandidate)
        {
            string key = ArgumentKey(new CommandArguments { ContainerId = equippedContainerId, InstanceId = incomingId, PositionKey = slotKey });
            var prior = CheckRequest(requestId, "Swap", key);
            if (prior != null) return prior;
            if (!TryLocation(incomingId, out var source)) return Failure("UnknownItem", "The incoming item does not exist.");
            if (!TryContainer(equippedContainerId, out var equipped) || !IsEquipped(equipped)) return Failure("InvalidEquipmentContainer", "An equipment or launcher container is required.");
            var backpack = containers[source.ContainerId];
            if (backpack.Kind != ItemContainerKind.Backpack) return Failure("RequiresBackpack", "The incoming item must be in a backpack.");
            if (backpack.OwnerId != equipped.OwnerId) return Failure("WrongOwner", "The backpack and equipment owners differ.");
            if (!ItemDataValidation.HasId(slotKey)) return Failure("InvalidPosition", "An equipment slot key is required.");
            string outgoingId = FindAt(equippedContainerId, slotKey);
            if (outgoingId == null && CountIn(equippedContainerId) >= equipped.Capacity) return Failure("ContainerFull", "The equipped container is full.");
            var candidates = OrderedIdsIn(equippedContainerId).Where(id => id != outgoingId)
                .Select(id => new KeyValuePair<string, ItemInstance>(locations[id].PositionKey, items[id].Clone())).ToList();
            candidates.Add(new KeyValuePair<string, ItemInstance>(slotKey, items[incomingId].Clone()));
            var candidateResult = ValidateCandidate(validateCandidate, candidates.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray());
            if (candidateResult != null) return candidateResult;
            // The incoming item frees the exact backpack position used by the outgoing item.
            if (outgoingId != null) locations[outgoingId] = source.Clone();
            locations[incomingId] = new ItemLocation { ContainerId = equippedContainerId, PositionKey = slotKey };
            return Record(requestId, "Swap", key, Success("Equipped", outgoingId == null ? new[] { incomingId } : new[] { incomingId, outgoingId }));
        }

        public ItemOperationResult Unequip(string requestId, string equippedContainerId, string slotKey, string backpackId, Func<ItemInstance[], string> validateCandidate)
        {
            string key = ArgumentKey(new CommandArguments { ContainerId = equippedContainerId, InstanceId = backpackId, PositionKey = slotKey });
            var prior = CheckRequest(requestId, "Unequip", key);
            if (prior != null) return prior;
            if (!TryContainer(equippedContainerId, out var equipped) || !IsEquipped(equipped)) return Failure("InvalidEquipmentContainer", "An equipment or launcher container is required.");
            if (!TryContainer(backpackId, out var backpack) || backpack.Kind != ItemContainerKind.Backpack) return Failure("RequiresBackpack", "The destination must be a backpack.");
            if (backpack.OwnerId != equipped.OwnerId) return Failure("WrongOwner", "The backpack and equipment owners differ.");
            if (!ItemDataValidation.HasId(slotKey)) return Failure("InvalidPosition", "An equipment slot key is required.");
            string outgoingId = FindAt(equippedContainerId, slotKey);
            if (outgoingId == null) return Failure("EmptySlot", "The equipment slot is empty.");
            if (CountIn(backpackId) >= backpack.Capacity) return Failure("ContainerFull", "The backpack has no free position.");
            var candidates = OrderedIdsIn(equippedContainerId).Where(id => id != outgoingId).Select(id => items[id].Clone()).ToArray();
            var candidateResult = ValidateCandidate(validateCandidate, candidates);
            if (candidateResult != null) return candidateResult;
            locations[outgoingId] = new ItemLocation { ContainerId = backpackId, PositionKey = ReservePosition(OccupiedPositions(backpackId)) };
            return Record(requestId, "Unequip", key, Success("Unequipped", new[] { outgoingId }));
        }

        ItemOperationResult ValidateCandidate(Func<ItemInstance[], string> validator, ItemInstance[] candidate)
        {
            if (validator == null) return null;
            validatingCandidate = true;
            try
            {
                string error = validator(candidate);
                return string.IsNullOrEmpty(error) ? null : Failure("CandidateRejected", error);
            }
            catch (Exception exception) { return Failure("CandidateValidationFailed", exception.Message); }
            finally { validatingCandidate = false; }
        }

        ItemOperationResult CheckRequest(string requestId, string operation, string key)
        {
            if (validatingCandidate) return Failure("Busy", "Mutation during candidate validation is not allowed.");
            if (!ItemDataValidation.HasId(requestId)) return Failure("InvalidRequest", "A durable request ID is required.");
            if (!receipts.TryGetValue(requestId, out var receipt)) return null;
            return receipt.Operation == operation && receipt.ArgumentKey == key
                ? receipt.Result.Clone() : Failure("RequestConflict", "The request ID was already used for a different operation.");
        }

        ItemOperationResult Record(string requestId, string operation, string key, ItemOperationResult result)
        {
            receipts.Add(requestId, new ItemRequestReceipt { RequestId = requestId, Operation = operation, ArgumentKey = key, Result = result.Clone() });
            return result.Clone();
        }

        static string ArgumentKey(CommandArguments arguments)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(arguments));
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }
        bool TryContainer(string id, out ItemContainerDefinition definition) { definition = null; return id != null && containers.TryGetValue(id, out definition); }
        bool TryLocation(string id, out ItemLocation location) { location = null; return id != null && locations.TryGetValue(id, out location); }
        static bool IsEquipped(ItemContainerDefinition container) => container.Kind == ItemContainerKind.Equipment || container.Kind == ItemContainerKind.Launcher;
        string FindAt(string containerId, string position, string exceptId = null) => locations.FirstOrDefault(pair => pair.Key != exceptId && pair.Value.ContainerId == containerId && pair.Value.PositionKey == position).Key;
        HashSet<string> OccupiedPositions(string containerId) => new HashSet<string>(locations.Values.Where(location => location.ContainerId == containerId).Select(location => location.PositionKey), StringComparer.Ordinal);
        static string ReservePosition(HashSet<string> occupied)
        {
            int index = 0;
            while (occupied.Contains(index.ToString(CultureInfo.InvariantCulture))) index++;
            string position = index.ToString(CultureInfo.InvariantCulture);
            occupied.Add(position);
            return position;
        }
        static ItemOperationResult Success(string code, string[] ids) => new ItemOperationResult { Success = true, Code = code, Message = "", InstanceIds = ids };
        static ItemOperationResult Failure(string code, string message) => new ItemOperationResult { Success = false, Code = code, Message = message };

        public ItemStoreSnapshot ExportSnapshot() => new ItemStoreSnapshot {
            SchemaVersion = 1,
            Containers = Containers(),
            Items = items.Values.OrderBy(item => item.InstanceId, StringComparer.Ordinal).Select(item => item.Clone()).ToArray(),
            Locations = locations.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ItemLocationRecord { InstanceId = pair.Key, Location = pair.Value.Clone() }).ToArray(),
            Receipts = receipts.Values.OrderBy(receipt => receipt.RequestId, StringComparer.Ordinal).Select(receipt => new ItemRequestReceipt {
                RequestId = receipt.RequestId, Operation = receipt.Operation, ArgumentKey = receipt.ArgumentKey, Result = receipt.Result.Clone() }).ToArray()
        };

        public static ItemStore FromSnapshot(ItemStoreSnapshot snapshot)
        {
            if (snapshot == null || snapshot.SchemaVersion != 1 || snapshot.Containers == null || snapshot.Items == null
                || snapshot.Locations == null || snapshot.Receipts == null) throw new InvalidDataException("Unsupported or incomplete item store snapshot.");
            var store = new ItemStore();
            foreach (var container in snapshot.Containers)
            {
                string error = ItemDataValidation.ValidateContainer(container);
                if (error != null || store.containers.ContainsKey(container.ContainerId)) throw new InvalidDataException(error ?? "Duplicate container ID.");
                store.containers.Add(container.ContainerId, container.Clone());
            }
            foreach (var item in snapshot.Items)
            {
                string error = ItemDataValidation.ValidateInstance(item);
                if (error != null || store.items.ContainsKey(item.InstanceId)) throw new InvalidDataException(error ?? "Duplicate item instance ID.");
                store.items.Add(item.InstanceId, item.Clone());
            }
            var occupied = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var container in store.containers.Keys) occupied.Add(container, new HashSet<string>(StringComparer.Ordinal));
            foreach (var record in snapshot.Locations)
            {
                if (record == null || !ItemDataValidation.HasId(record.InstanceId) || !store.items.ContainsKey(record.InstanceId)
                    || store.locations.ContainsKey(record.InstanceId) || record.Location == null
                    || !ItemDataValidation.HasId(record.Location.PositionKey) || !store.TryContainer(record.Location.ContainerId, out var container))
                    throw new InvalidDataException("An item has an invalid, duplicate, or missing location.");
                var positions = occupied[container.ContainerId];
                if (!positions.Add(record.Location.PositionKey) || positions.Count > container.Capacity) throw new InvalidDataException("A container has a duplicate position or exceeds capacity.");
                store.locations.Add(record.InstanceId, record.Location.Clone());
            }
            if (store.locations.Count != store.items.Count) throw new InvalidDataException("Every item must have exactly one location.");
            foreach (var receipt in snapshot.Receipts)
            {
                if (receipt == null || !ItemDataValidation.HasId(receipt.RequestId) || !ItemDataValidation.HasId(receipt.ArgumentKey)
                    || (receipt.Operation != "Grant" && receipt.Operation != "Move" && receipt.Operation != "Swap" && receipt.Operation != "Unequip")
                    || receipt.Result == null || !receipt.Result.Success || !ItemDataValidation.HasId(receipt.Result.Code)
                    || receipt.Result.InstanceIds == null || receipt.Result.InstanceIds.Length == 0
                    || receipt.Result.InstanceIds.Any(id => !ItemDataValidation.HasId(id))
                    || receipt.Result.InstanceIds.Distinct(StringComparer.Ordinal).Count() != receipt.Result.InstanceIds.Length
                    || ((receipt.Operation == "Move" || receipt.Operation == "Unequip") && receipt.Result.InstanceIds.Length != 1)
                    || (receipt.Operation == "Swap" && receipt.Result.InstanceIds.Length > 2)
                    || store.receipts.ContainsKey(receipt.RequestId)) throw new InvalidDataException("An operation receipt is malformed or duplicated.");
                store.receipts.Add(receipt.RequestId, new ItemRequestReceipt { RequestId = receipt.RequestId,
                    Operation = receipt.Operation, ArgumentKey = receipt.ArgumentKey, Result = receipt.Result.Clone() });
            }
            return store;
        }
    }
}
