using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CelToon.CustomEquipment.Definitions;
using CelToon.CustomEquipment.Runtime;
using CelToon.CustomItem.Adapters;
using CelToon.CustomItem.Contracts;
using CelToon.CustomItem.Runtime;
using CelToon.CustomLauncher.Definitions;
using CelToon.CustomLauncher.Runtime;
using UnityEngine;

namespace CelToon.CustomItem.Samples
{
    // The sample owns presentation and test bootstrap; circulation and equip rules remain in Items.
    public sealed class ItemRangeWorkbench : MonoBehaviour
    {
        public ItemSpecificationCatalog Catalog;
        public LauncherHost Launcher;
        public EquipmentHost Equipment;
        public Transform Player;
        public Camera RangeCamera;
        public LauncherRecipeDefinition LauncherRecipe;
        public EquipmentRecipeDefinition EquipmentRecipe;
        public WorldItemView[] WorldViews = Array.Empty<WorldItemView>();
        public float PickupDistance = 2.5f;
        public ItemEquipmentService Service { get; private set; }
        public ItemStore Store => Service?.Store;
        public bool ItemMode => Service != null && Service.Active;
        public bool Initialized => Service != null;
        public string Message { get; private set; } = "进入物品测试后，可拾取实物、装配和存取仓库。";
        public string SavePath => Path.Combine(Application.persistentDataPath, "CustomItem", "CombatRangeItems.json");
        public float Scale => Mathf.Max(.1f, Mathf.Min(Screen.width / 1440f, Screen.height / 900f));
        private string selectedId;
        private bool enteredOnce;
        private int listView;
        private Vector2 scroll;
        private Action pendingAction;
        private Font font;
        private GUIStyle label, small, title, button;
#if UNITY_EDITOR
        private readonly Dictionary<string, Rect> controls = new Dictionary<string, Rect>();
        public bool TryGetControlRect(string id, out Rect rect) => controls.TryGetValue(id, out rect);
        private void Record(string id)
        {
            if (Event.current.type != EventType.Repaint) return;
            var rect = GUILayoutUtility.GetLastRect();
            var origin = new Vector2(26, 74);
            var min = GUIUtility.GUIToScreenPoint(rect.min) + origin * (Scale - 1);
            var max = GUIUtility.GUIToScreenPoint(rect.max) + origin * (Scale - 1);
            controls[id] = new Rect(min, max - min);
        }
#else
        private void Record(string id) { }
#endif
        public bool TryInitialize()
        {
            if (Initialized) return true;
            if (Catalog == null || Launcher == null || Equipment == null || Player == null
                || LauncherRecipe == null || EquipmentRecipe == null) return false;
            try {
                Service = new ItemEquipmentService(new ItemStore(), Catalog, Launcher, Equipment, LauncherRecipe, EquipmentRecipe);
                var factory = new ItemFactory(Catalog);
                var starter = Store.Grant("range.starter", new[] { factory.Create(Catalog.StarterSpecificationId) }, ItemEquipmentService.BackpackId);
                if (!starter.Success) throw new InvalidOperationException(starter.Message);
                foreach (var sample in Catalog.WorldSamples) {
                    var grant = Store.Grant("range.world." + sample.SpawnId,
                        new[] { factory.Create(sample.SpecificationId) }, ItemEquipmentService.WorldId);
                    if (!grant.Success) throw new InvalidOperationException(grant.Message);
                }
                RefreshWorldBindings();
                Message = "实物案例已就绪。自由装配与物品装配可分别测试；R 只重置战斗。";
                return true;
            }
            catch (Exception exception) {
                Service = null;
                Message = "物品测试初始化失败：" + exception.Message;
                Debug.LogException(exception, this); return false;
            }
        }
        public bool EnterItemMode()
        {
            if (!TryInitialize()) return false;
            bool entered = Report(Service.EnterItemMode());
            if (entered) {
                if (!enteredOnce && !Store.ItemsIn(ItemEquipmentService.LauncherId).Any()) {
                    var starter = Store.GetReceipt("range.starter");
                    if (starter?.Success == true && starter.InstanceIds.Length > 0
                        && Store.GetLocation(starter.InstanceIds[0])?.ContainerId == ItemEquipmentService.BackpackId)
                        Report(Service.Equip(starter.InstanceIds[0]));
                }
                enteredOnce = true; listView = 1; scroll = Vector2.zero;
            }
            return entered;
        }
        public bool ExitItemMode()
        { return Initialized && Report(Service.ExitItemMode()); }
        public bool Equip(string instanceId)
        { return Initialized && Report(Service.Equip(instanceId)); }
        public bool Unequip(string instanceId)
        {
            var item = Store?.GetItem(instanceId);
            return item != null && Report(Service.Unequip(item.DefinitionRef.Family, ItemEquipmentService.GetSlotId(item)));
        }
        public bool PutInWarehouse(string instanceId)
        { return Initialized && Report(Service.PutInWarehouse(instanceId)); }
        public bool Retrieve(string instanceId)
        { return Initialized && Report(Service.Retrieve(instanceId)); }
        public bool Pickup(string instanceId)
        {
            if (!ItemMode) { Message = "请先点击「进入物品测试」。"; return false; }
            var view = FindView(instanceId);
            if (view == null || !view.Available) { Message = "该物品不在靶场地面。"; return false; }
            if (Player == null || Vector3.Distance(Player.position, view.transform.position) > PickupDistance) {
                Message = "距离超过 " + PickupDistance.ToString("0.0") + " m，请靠近后拾取。"; return false;
            }
            bool pickedUp = Report(Service.Pickup(instanceId));
            if (pickedUp) { selectedId = instanceId; listView = 0; scroll = Vector2.zero; }
            return pickedUp;
        }
        public bool PickupNearest()
        {
            if (!TryInitialize()) return false;
            RefreshWorldBindings();
            WorldItemView nearest = null; float distance = PickupDistance;
            foreach (var view in WorldViews) {
                if (view == null || !view.Available) continue;
                float candidate = Vector3.Distance(Player.position, view.transform.position);
                if (candidate <= distance) { distance = candidate; nearest = view; }
            }
            if (nearest == null) { Message = "附近没有可拾取物品（范围 " + PickupDistance.ToString("0.0") + " m）。"; return false; }
            return nearest.TryPickup();
        }
        public bool SaveItems()
        { return Initialized && Report(Service.Save(SavePath)); }
        public bool LoadItems()
        {
            if (!Initialized) return false;
            bool loaded = Report(Service.Load(SavePath));
            if (loaded) { enteredOnce = true; selectedId = null; RefreshWorldBindings(); }
            return loaded;
        }
        public bool HasEquipmentCore(string slotId)
        { return Initialized && Store.ItemsIn(ItemEquipmentService.EquipmentId).Any(item => ItemEquipmentService.GetSlotId(item) == slotId); }
        private bool Report(ItemOperationResult result)
        {
            Message = result.Message;
            RefreshWorldBindings(); return result.Success;
        }
        public void RefreshWorldBindings()
        {
            if (!Initialized || WorldViews == null) return;
            foreach (var view in WorldViews) {
                if (view == null) continue;
                var receipt = Store.GetReceipt("range.world." + view.SpawnId);
                string id = receipt?.Success == true && receipt.InstanceIds.Length > 0 ? receipt.InstanceIds[0] : null;
                view.Bind(this, id);
            }
        }
        private WorldItemView FindView(string id)
        { return WorldViews == null ? null : Array.Find(WorldViews, view => view != null && view.ItemId == id); }
        public bool SelectWorldItem(Vector3 screenPoint)
        {
            if (!Initialized || RangeCamera == null || !Physics.Raycast(RangeCamera.ScreenPointToRay(screenPoint), out var hit,
                100, ~0, QueryTriggerInteraction.Collide)) return false;
            var view = hit.collider.GetComponentInParent<WorldItemView>();
            if (view == null || !view.Available || FindView(view.ItemId) != view) return false;
            selectedId = view.ItemId; listView = 1; scroll = Vector2.zero; return true;
        }
        private void Queue(Action action) { if (pendingAction == null) pendingAction = action; }
        private void LateUpdate()
        { var action = pendingAction; pendingAction = null; action?.Invoke(); }
        private void Styles()
        {
            if (label != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 16);
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16, wordWrap = true };
            small = new GUIStyle(label) { fontSize = 13 };
            title = new GUIStyle(label) { fontSize = 21, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 14, wordWrap = true, alignment = TextAnchor.MiddleLeft };
        }
        public void DrawLockedPanel()
        {
            Styles(); var previous = GUI.matrix; GUI.matrix = Matrix4x4.Scale(Vector3.one * Scale);
            GUI.Box(new Rect(12, 12, 376, 874), GUIContent.none);
            GUILayout.BeginArea(new Rect(26, 74, 348, 790));
            GUILayout.Label("正在使用物品装配", title);
            GUILayout.Label("在「物品」页选择背包中的实例进行换装，或返回自由装配继续试用模板。", label);
            if (GUILayout.Button("返回自由装配", button, GUILayout.Height(38))) Queue(() => ExitItemMode());
            Record("item.exit");
            GUILayout.Label(Message, small);
            GUILayout.EndArea(); GUI.matrix = previous;
        }
        public void DrawPanel()
        {
            Styles(); TryInitialize();
            var previous = GUI.matrix; GUI.matrix = Matrix4x4.Scale(Vector3.one * Scale);
            GUI.Box(new Rect(12, 12, 376, 874), GUIContent.none);
            GUILayout.BeginArea(new Rect(26, 74, 348, 790));
            GUILayout.Label("自定义物品 / ITEM", title);
            GUILayout.Label(ItemMode ? "物品装配 · 每件实例只有一个位置" : "自由装配 · 已装配模板保持原配置", small);
            bool mode = ItemMode;
            if (GUILayout.Button(mode ? "返回自由装配" : "进入物品测试", button, GUILayout.Height(38))) {
                Queue(() => { if (mode) ExitItemMode(); else EnterItemMode(); });
            }
            Record(mode ? "item.exit" : "item.enter");
            if (!Initialized) { GUILayout.Label(Message, label); GUILayout.EndArea(); GUI.matrix = previous; return; }
            GUILayout.BeginHorizontal();
            bool enabled = GUI.enabled; GUI.enabled = ItemMode;
            if (GUILayout.Button("拾取最近物品 [E]", button, GUILayout.Height(32))) Queue(() => PickupNearest());
            Record("item.pickup-nearest"); GUI.enabled = enabled;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("保存物品", button, GUILayout.Height(30))) Queue(() => SaveItems()); Record("item.save");
            if (GUILayout.Button("读取物品", button, GUILayout.Height(30))) Queue(() => LoadItems()); Record("item.load");
            GUILayout.EndHorizontal();
            GUILayout.Label(Message, small);
            GUILayout.Label("拾取范围 " + PickupDistance.ToString("0.0") + " m · 存档只记录物品，读取不会补满人物资源。", small);
            GUILayout.BeginHorizontal();
            var views = new[] { "背包", "地面", "装配", "仓库" };
            for (int i = 0; i < views.Length; i++) {
                if (GUILayout.Button((listView == i ? "● " : "") + views[i], button, GUILayout.Height(30))) { listView = i; scroll = Vector2.zero; }
                Record("item.view." + i);
            }
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll);
            var selected = Store.GetItem(selectedId);
            if (selected != null) {
                GUILayout.Space(6); GUILayout.Label("实例详情", title); DrawItemTitle(selected);
                GUILayout.Label(selected.Description, label);
                foreach (var affix in selected.Content.Affixes) {
                    var parameters = string.Join(", ", affix.Parameters.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)));
                    GUILayout.Label(affix.AffixId + " · " + affix.Role + "\n参数：" + parameters + "\n预设：" + affix.ParameterPresetId, small);
                }
                GUILayout.Label("完整实例 ID：\n" + selected.InstanceId, small);
            }
            if (listView == 0) DrawContainer("背包", ItemEquipmentService.BackpackId, 16);
            else if (listView == 1) DrawContainer("靶场地面", ItemEquipmentService.WorldId, -1);
            else if (listView == 2) {
                DrawContainer("已装配 · 装备", ItemEquipmentService.EquipmentId, 4);
                DrawContainer("已装配 · 发射器", ItemEquipmentService.LauncherId, 8);
            }
            else DrawContainer("仓库", ItemEquipmentService.WarehouseId, 32);
            GUILayout.EndScrollView(); GUILayout.EndArea(); GUI.matrix = previous;
        }
        private void DrawContainer(string name, string containerId, int capacity)
        {
            var items = Store.ItemsIn(containerId).ToArray();
            GUILayout.Space(6); GUILayout.Label(name + "  " + items.Length + (capacity >= 0 ? "/" + capacity : ""), label);
            if (items.Length == 0) { GUILayout.Label("空", small); return; }
            for (int index = 0; index < items.Length; index++) {
                var item = items[index]; var color = GUI.contentColor; GUI.contentColor = RarityColor(item.Rarity);
                if (GUILayout.Button((selectedId == item.InstanceId ? "● " : "") + RarityName(item.Rarity) + " · " + item.DisplayName
                    + "\n" + ItemEquipmentService.GetSlotId(item) + " · #" + ShortId(item.InstanceId), button, GUILayout.MinHeight(46))) { selectedId = item.InstanceId; scroll = Vector2.zero; }
                Record("item.select." + containerId + "." + index); GUI.contentColor = color;
                GUILayout.BeginHorizontal(); var enabled = GUI.enabled; GUI.enabled = ItemMode;
                if (containerId == ItemEquipmentService.BackpackId) {
                    if (GUILayout.Button("装配", button)) Queue(() => Equip(item.InstanceId)); Record("item.equip." + index);
                    if (GUILayout.Button("入仓", button)) Queue(() => PutInWarehouse(item.InstanceId)); Record("item.warehouse." + index);
                }
                else if (containerId == ItemEquipmentService.WarehouseId) {
                    if (GUILayout.Button("取回背包", button)) Queue(() => Retrieve(item.InstanceId)); Record("item.retrieve." + index);
                }
                else if (containerId == ItemEquipmentService.WorldId) {
                    var view = FindView(item.InstanceId); float distance = view != null ? Vector3.Distance(Player.position, view.transform.position) : float.PositiveInfinity;
                    GUI.enabled = ItemMode && view != null && view.Available && distance <= PickupDistance;
                    if (GUILayout.Button("拾取 · " + distance.ToString("0.0") + " m", button)) Queue(() => Pickup(item.InstanceId));
                    Record("item.pickup." + (view != null ? view.SpawnId : index.ToString()));
                }
                else {
                    if (GUILayout.Button("卸回背包", button)) Queue(() => Unequip(item.InstanceId)); Record("item.unequip." + containerId + "." + index);
                }
                GUI.enabled = enabled; GUILayout.EndHorizontal();
            }
        }
        private void DrawItemTitle(ItemInstance item)
        {
            var color = GUI.contentColor; GUI.contentColor = RarityColor(item.Rarity);
            GUILayout.Label(RarityName(item.Rarity) + " · " + item.DisplayName, label); GUI.contentColor = color;
        }
        public void DrawWorldLabels()
        {
            if (!Initialized || RangeCamera == null || WorldViews == null) return;
            Styles(); var previous = GUI.matrix; GUI.matrix = Matrix4x4.Scale(Vector3.one * Scale);
            foreach (var view in WorldViews) {
                if (view == null || !view.Available) continue;
                var item = Store.GetItem(view.ItemId); if (item == null) continue;
                var point = RangeCamera.WorldToScreenPoint(view.transform.position + Vector3.up * .55f);
                if (point.z <= 0 || point.x / Scale < 412 || point.x / Scale > 1030) continue;
                float distance = Vector3.Distance(Player.position, view.transform.position);
                var color = GUI.contentColor; GUI.contentColor = RarityColor(item.Rarity);
                GUI.Label(new Rect(point.x / Scale - 70, (Screen.height - point.y) / Scale - 24, 145, 50),
                    RarityName(item.Rarity).Substring(0, 1) + " · " + item.DisplayName + (ItemMode && distance <= PickupDistance ? "\n[E] 拾取" : ""), small);
                GUI.contentColor = color;
            }
            GUI.matrix = previous;
        }
        public static string RarityName(ItemRarity rarity)
        { return new[] { "白 / 普通", "绿 / 优良", "蓝 / 稀有", "紫 / 史诗", "橙 / 传奇" }[(int)rarity]; }
        public static Color RarityColor(ItemRarity rarity)
        {
            switch (rarity) {
                case ItemRarity.Uncommon: return new Color(.4f, .9f, .45f);
                case ItemRarity.Rare: return new Color(.4f, .72f, 1);
                case ItemRarity.Epic: return new Color(.82f, .5f, 1);
                case ItemRarity.Legendary: return new Color(1, .68f, .2f);
                default: return new Color(.9f, .93f, .95f);
            }
        }
        private static string ShortId(string id) => string.IsNullOrEmpty(id) ? "?" : id.Substring(0, Mathf.Min(8, id.Length));
        private void OnDestroy() { if (font != null) Destroy(font); }
    }
}
