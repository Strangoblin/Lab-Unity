using System;
using System.Collections.Generic;
using System.Linq;
using CelToon.CustomItem.Contracts;
using CelToon.CustomItem.Runtime;
using CelToon.CustomEquipment.Compilation;
using CelToon.CustomEquipment.Definitions;
using CelToon.CustomEquipment.Runtime;
using CelToon.CustomLauncher.Compilation;
using CelToon.CustomLauncher.Definitions;
using CelToon.CustomLauncher.Runtime;
using UnityEngine;

namespace CelToon.CustomItem.Adapters
{
    // The only bridge: inventory owns item identity; domain compilers own combat plans.
    public sealed class ItemEquipmentService
    {
        public const string BackpackId = "player.backpack", WarehouseId = "player.warehouse", LauncherId = "player.launcher",
            EquipmentId = "player.equipment", WorldId = "range.world";
        public ItemStore Store { get; private set; }
        public bool Active { get; private set; }
        private readonly ItemFactory factory;
        private readonly LauncherHost launcher;
        private readonly EquipmentHost equipment;
        private readonly LauncherRecipeDefinition launcherRecipe;
        private readonly EquipmentRecipeDefinition equipmentRecipe;
        private LauncherLoadout freeLauncher;
        private EquipmentLoadout freeEquipment;

        public ItemEquipmentService(ItemStore store, ItemSpecificationCatalog catalog, LauncherHost launcher, EquipmentHost equipment,
            LauncherRecipeDefinition launcherRecipe, EquipmentRecipeDefinition equipmentRecipe)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            this.launcher = launcher != null ? launcher : throw new ArgumentNullException(nameof(launcher));
            this.equipment = equipment != null ? equipment : throw new ArgumentNullException(nameof(equipment));
            this.launcherRecipe = launcherRecipe != null ? launcherRecipe : throw new ArgumentNullException(nameof(launcherRecipe));
            this.equipmentRecipe = equipmentRecipe != null ? equipmentRecipe : throw new ArgumentNullException(nameof(equipmentRecipe));
            factory = new ItemFactory(catalog);
            Register(BackpackId, ItemContainerKind.Backpack, 16); Register(WarehouseId, ItemContainerKind.Warehouse, 32);
            Register(LauncherId, ItemContainerKind.Launcher, 8); Register(EquipmentId, ItemContainerKind.Equipment, 4);
            Register(WorldId, ItemContainerKind.World, int.MaxValue);
        }

        private void Register(string id, ItemContainerKind kind, int capacity)
        {
            var r = Store.RegisterContainer(new ItemContainerDefinition { ContainerId = id, OwnerId = "range.player", Kind = kind, Capacity = capacity });
            if (!r.Success) throw new ArgumentException(r.Message);
        }

        public ItemOperationResult EnterItemMode()
        {
            if (Active) return Success("已处于物品测试模式");
            string schema = ValidateContainers(Store);
            if (schema != null) return Failure("mode.containers", schema);
            if (launcher.Loadout == null || equipment.Loadout == null) return Failure("mode.backup", "自由装配草稿缺失，无法进入物品测试");
            if (!CompileStored(Store, out var l, out var e, out string problem)) return Failure("mode.compile", problem);
            freeLauncher = launcher.Loadout; freeEquipment = equipment.Loadout;
            launcher.TryCommitCompiled(l); equipment.TryCommitCompiled(e); Active = true;
            return Success("已进入物品测试：装配只读取实际持有的物品");
        }

        public ItemOperationResult ExitItemMode()
        {
            if (!Active) return Success("已处于自由装配模式");
            var l = LauncherCompiler.Compile(freeLauncher); var e = EquipmentCompiler.Compile(freeEquipment);
            if (!l.Success || !e.Success) return Failure("mode.restore", "原自由装配无法恢复，请检查制作定义");
            launcher.TryEquip(freeLauncher); equipment.TryEquip(freeEquipment); Active = false;
            return Success("已恢复自由装配；物品与位置保持");
        }

        public ItemOperationResult Equip(string instanceId)
        {
            if (!Active) return Failure("mode.inactive", "请先进入物品测试模式");
            var item = Store.GetItem(instanceId);
            if (!factory.Validate(item, out string problem)) return Failure("item.invalid", problem);
            string slot = GetSlotId(item);
            bool isLauncher = item.DefinitionRef.Family == ItemFamily.LauncherPart;
            CompileResult l = null; EquipmentCompileResult e = null;
            var result = Store.TrySwap(Request(), instanceId, isLauncher ? LauncherId : EquipmentId, slot, candidate => {
                var p = ValidateCandidate(candidate, item.DefinitionRef.Family, Store);
                if (p != null) return p;
                if (isLauncher) { l = CompileLauncher(candidate); return l.Success ? null : LauncherProblem(l); }
                e = CompileEquipment(candidate); return e.Success ? null : string.Join(" / ", e.Diagnostics);
            });
            if (result.Success) { if (isLauncher) launcher.TryCommitCompiled(l); else equipment.TryCommitCompiled(e); }
            return result;
        }

        public ItemOperationResult Unequip(ItemFamily family, string slotId)
        {
            if (!Active) return Failure("mode.inactive", "请先进入物品测试模式");
            if (!Enum.IsDefined(typeof(ItemFamily), family)) return Failure("item.family", "未知物品领域");
            bool isLauncher = family == ItemFamily.LauncherPart;
            CompileResult l = null; EquipmentCompileResult e = null;
            var result = Store.Unequip(Request(), isLauncher ? LauncherId : EquipmentId, slotId, BackpackId, candidate => {
                var p = ValidateCandidate(candidate, family, Store);
                if (p != null) return p;
                if (isLauncher) { l = CompileLauncher(candidate); return l.Success ? null : LauncherProblem(l); }
                e = CompileEquipment(candidate); return e.Success ? null : string.Join(" / ", e.Diagnostics);
            });
            if (result.Success) { if (isLauncher) launcher.TryCommitCompiled(l); else equipment.TryCommitCompiled(e); }
            return result;
        }

        public ItemOperationResult Pickup(string id) => Transfer(id, WorldId, BackpackId);
        public ItemOperationResult PutInWarehouse(string id) => Transfer(id, BackpackId, WarehouseId);
        public ItemOperationResult Retrieve(string id) => Transfer(id, WarehouseId, BackpackId);
        private ItemOperationResult Transfer(string id, string from, string to)
        {
            var location = Store.GetLocation(id);
            if (location == null || location.ContainerId != from) return Failure("item.location", "物品已不在预期位置");
            return Store.Move(Request(), id, to);
        }

        public ItemOperationResult Save(string path)
        {
            try { ItemSaveService.Save(path, Store); return Success("已保存物品、装配位置和领取记录"); }
            catch (Exception ex) { return Failure("save.failed", ex.Message); }
        }

        public ItemOperationResult Load(string path)
        {
            try
            {
                var candidate = ItemSaveService.Load(path);
                string schema = ValidateContainers(candidate);
                if (schema != null) return Failure("load.containers", schema);
                // Inactive mode can keep unknown contents for repair; active load must have a valid whole build.
                if (Active)
                {
                    if (!CompileStored(candidate, out var l, out var e, out string problem)) return Failure("load.compile", problem);
                    launcher.TryCommitCompiled(l); equipment.TryCommitCompiled(e);
                }
                Store = candidate; return Success("已读取物品存档，当前资源和冷却保持");
            }
            catch (Exception ex) { return Failure("load.failed", ex.Message); }
        }

        public static string GetSlotId(ItemInstance item)
        {
            if (item?.Content == null || item.DefinitionRef == null || item.Content.PayloadVersion != 1) return "";
            try
            {
                if (item.DefinitionRef.Family == ItemFamily.LauncherPart && item.Content.PayloadFormat == "launcher-part.v1") {
                    var p = JsonUtility.FromJson<LauncherItemPayload>(item.Content.PayloadJson);
                    return p != null && CelToon.CustomLauncher.Contracts.LauncherSlots.Supported(p.Kind) ? CelToon.CustomLauncher.Contracts.LauncherSlots.Id(p.Kind) : "";
                }
                if (item.DefinitionRef.Family == ItemFamily.EquipmentCore && item.Content.PayloadFormat == "equipment-core.v1") {
                    var p = JsonUtility.FromJson<EquipmentItemPayload>(item.Content.PayloadJson);
                    return p != null && CelToon.CustomEquipment.Contracts.EquipmentSlots.Supported(p.Kind) ? CelToon.CustomEquipment.Contracts.EquipmentSlots.Id(p.Kind) : "";
                }
            }
            catch (ArgumentException) { }
            return "";
        }

        private bool CompileStored(ItemStore store, out CompileResult l, out EquipmentCompileResult e, out string problem)
        {
            l = null; e = null;
            var li = store.ItemsIn(LauncherId).ToArray(); var ei = store.ItemsIn(EquipmentId).ToArray();
            foreach (var item in li.Concat(ei))
            {
                if (!factory.Validate(item, out problem)) return false;
                if (store.GetLocation(item.InstanceId)?.PositionKey != GetSlotId(item)) { problem = "装配位置与物品类别不符"; return false; }
            }
            problem = ValidateCandidate(li, ItemFamily.LauncherPart, store) ?? ValidateCandidate(ei, ItemFamily.EquipmentCore, store);
            if (problem != null) return false;
            l = CompileLauncher(li); e = CompileEquipment(ei);
            problem = !l.Success ? LauncherProblem(l) : !e.Success ? string.Join(" / ", e.Diagnostics) : null;
            return problem == null;
        }

        private string ValidateCandidate(ItemInstance[] items, ItemFamily family, ItemStore store)
        {
            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (!factory.Validate(item, out string p)) return item?.DisplayName + "：" + p;
                if (item.DefinitionRef.Family != family || !slots.Add(GetSlotId(item))) return "物品领域或槽位重复";
            }
            var unique = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in items)
                foreach (var a in item.Content.Affixes)
                    foreach (var g in a.UniqueGroupIds)
                    {
                        string key = a.UniqueScope + ":" + g;
                        if (unique.TryGetValue(key, out string previous)) return "词条冲突：" + previous + " / " + item.DisplayName + "（" + g + "）";
                        unique.Add(key, item.DisplayName);
                    }
            return null;
        }

        private CompileResult CompileLauncher(ItemInstance[] items) => LauncherCompiler.Compile(launcherRecipe, items.Select(ItemFactory.ResolveLauncher).ToArray());
        private EquipmentCompileResult CompileEquipment(ItemInstance[] items) => EquipmentCompiler.Compile(equipmentRecipe, items.Select(ItemFactory.ResolveEquipment).ToArray());
        private static string LauncherProblem(CompileResult r) => string.Join(" / ", r.Diagnostics.Select(d => d.ToString()));
        private static string Request() => Guid.NewGuid().ToString("N");
        private static string ValidateContainers(ItemStore store)
        {
            var ids = new[] { BackpackId, WarehouseId, LauncherId, EquipmentId, WorldId };
            var kinds = new[] { ItemContainerKind.Backpack, ItemContainerKind.Warehouse, ItemContainerKind.Launcher, ItemContainerKind.Equipment, ItemContainerKind.World };
            var capacities = new[] { 16, 32, 8, 4, int.MaxValue };
            for (int i = 0; i < ids.Length; i++) {
                var c = store.GetContainer(ids[i]);
                if (c == null || c.OwnerId != "range.player" || c.Kind != kinds[i] || c.Capacity != capacities[i])
                    return "物品存档容器不符合当前玩家规格：" + ids[i];
            }
            return null;
        }
        private static ItemOperationResult Success(string message) => new ItemOperationResult { Success = true, Code = "ok", Message = message, InstanceIds = Array.Empty<string>() };
        private static ItemOperationResult Failure(string code, string message) => new ItemOperationResult { Success = false, Code = code, Message = message, InstanceIds = Array.Empty<string>() };
    }
}
