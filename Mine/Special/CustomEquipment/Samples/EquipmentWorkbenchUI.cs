using System;
using System.Collections.Generic;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Definitions;
using CelToon.CustomEquipment.Runtime;
using UnityEngine;

namespace CelToon.CustomEquipment.Samples
{
    public sealed class EquipmentWorkbenchUI : MonoBehaviour
    {
        public EquipmentHost Host;
        public EquipmentCatalog Catalog;
        public EquipmentRecipeDefinition InitialRecipe;
        public bool DrawAutomatically = true;
        public bool Embedded;
        private EquipmentKind? openKind;
        private bool recipesOpen;
        private string search = "", message = "选择槽位即可换装；空配件可清空槽位";
        private Vector2 scroll;
        private Font font;
        private GUIStyle label, small, title, button;
        public bool PopupOpen => recipesOpen || openKind.HasValue;
        public float Scale => Mathf.Max(.1f, Mathf.Min(Screen.width / 1440f, Screen.height / 900f));
#if UNITY_EDITOR
        private readonly Dictionary<string, Rect> controlRects = new Dictionary<string, Rect>();
        public bool TryGetControlRect(string id, out Rect rect) => controlRects.TryGetValue(id, out rect);
        public void RecordControlRect(string id)
        {
            if (Event.current.type != EventType.Repaint) return;
            var rect = GUILayoutUtility.GetLastRect(); var min = GUIUtility.GUIToScreenPoint(rect.min); var max = GUIUtility.GUIToScreenPoint(rect.max);
            // Player GUIToScreenPoint scales the control, but leaves the outer BeginArea origin unscaled.
            var origin = id.StartsWith("test.", StringComparison.Ordinal) ? new Vector2(1066, 26)
                : id.StartsWith("slot.", StringComparison.Ordinal) || id == "recipe.menu" ? new Vector2(26, Embedded ? 74 : 24) : new Vector2(418, 75);
            min += origin * (Scale - 1); max += origin * (Scale - 1);
            controlRects[id] = new Rect(min, max - min);
        }
#endif
        private void Start()
        {
            if (Host == null) Host = GetComponentInChildren<EquipmentHost>(true);
            if (Host?.State?.Plan == null && InitialRecipe != null) SelectRecipe(InitialRecipe);
        }
        public void OpenSlot(EquipmentKind kind)
        { if (!EquipmentSlots.Supported(kind)) return; recipesOpen = false; openKind = kind; search = ""; scroll = Vector2.zero; }
        public void ClosePopup() { recipesOpen = false; openKind = null; }
        public bool SelectRecipe(EquipmentRecipeDefinition recipe)
        {
            if (Host == null || recipe == null) return false;
            bool success = Host.TryEquip(recipe.CreateLoadout());
            message = success ? "已应用：" + recipe.DisplayName + "（当前资源不补满）" : Host.LastCompilation.Diagnostics[0];
            if (success) ClosePopup(); return success;
        }
        public bool SelectPart(EquipmentKind kind, EquipmentPartDefinition part)
        {
            if (Host?.Loadout == null || !EquipmentSlots.Supported(kind)) return false;
            if (part != null && part.Kind != kind) { message = "配件类别与槽位不匹配"; return false; }
            bool success = Host.TryEquip(Host.Loadout.WithPart(kind, part));
            message = success ? EquipmentSlots.Name(kind) + "：" + (part != null ? part.DisplayName : "空配件") : Host.LastCompilation.Diagnostics[0];
            if (success) ClosePopup(); return success;
        }
        public EquipmentPartDefinition CurrentPart(EquipmentKind kind) => Host?.Loadout?.Find(kind)?.Part;
        private static string CoreSummary(EquipmentKind kind, EquipmentPartDefinition part)
        {
            if (part == null) return EquipmentSlots.Summary(kind);
            var primary = new List<string>();
            foreach (var step in part.PrimaryOperations) primary.Add(step.Operation == EquipmentOperation.HealthScale ? "上限 ×" + step.A.ToString("0.##") : "上限 +" + step.A.ToString("0.##"));
            return string.Join(" / ", primary) + "\n副词条 " + (part.Affixes?.Length ?? 0) + " 项";
        }
        public bool BlocksScreenPoint(Vector3 mouse)
        {
            var p = new Vector2(mouse.x / Scale, (Screen.height - mouse.y) / Scale);
            return new Rect(12, 12, 376, 874).Contains(p) || new Rect(1050, 12, 378, 874).Contains(p)
                || (PopupOpen && new Rect(402, 60, 630, 790).Contains(p));
        }
        private void Styles()
        {
            if (label != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 16);
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16, wordWrap = true };
            small = new GUIStyle(label) { fontSize = 13 };
            title = new GUIStyle(label) { fontSize = 23, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 15, wordWrap = true, alignment = TextAnchor.MiddleLeft };
        }
        private void OnGUI()
        { if (DrawAutomatically) DrawPanel(); }
        public void DrawPanel(bool includeOverlay = true)
        {
            var storage = GetComponent<StorageController>();
            if (storage != null) storage.HandleGUIInput(Event.current);
            Styles(); var previous = GUI.matrix; GUI.matrix = Matrix4x4.Scale(Vector3.one * Scale);
            GUI.Box(new Rect(12, 12, 376, 874), GUIContent.none);
            GUILayout.BeginArea(new Rect(26, Embedded ? 74 : 24, 348, Embedded ? 795 : 845));
            GUILayout.Label("自定义装备", title);
            GUILayout.Label("生命 · 护盾 · 体力 · 能量，各对应一件核心", small);
            if (GUILayout.Button("配方：" + (Host?.Loadout?.Recipe != null ? Host.Loadout.Recipe.DisplayName : "未选择") + "  ▾", button, GUILayout.Height(40)))
            { recipesOpen = true; openKind = null; search = ""; scroll = Vector2.zero; }
#if UNITY_EDITOR
            RecordControlRect("recipe.menu");
#endif
            GUILayout.Space(8);
            for (int row = 0; row < EquipmentSlots.Count / 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 2; col++)
                {
                    var kind = EquipmentSlots.At(row * 2 + col); var part = CurrentPart(kind);
                    if (GUILayout.Button(EquipmentSlots.Name(kind) + "\n" + (part != null ? part.DisplayName : "空配件") + "\n" + CoreSummary(kind, part), button, GUILayout.Width(170), GUILayout.Height(94))) OpenSlot(kind);
#if UNITY_EDITOR
                    RecordControlRect("slot." + kind);
#endif
                }
                GUILayout.EndHorizontal(); GUILayout.Space(4);
            }
            GUILayout.Label(message, small); GUILayout.Space(10);
            if (Host?.State?.Plan != null)
            {
                var s = Host.State.Plan.Stats;
                GUILayout.Label("装配属性", title);
                GUILayout.Label($"生命上限 {s.MaxHealth:F0} · 护甲 {s.Armor:F1}\n受到伤害 ×{s.DamageScale:F2}\n护盾上限 {s.MaxShield:F0} · 回复 {s.ShieldRegen:F1}/s\n护盾等待 {s.ShieldDelay:F1}s · 生命回复 {s.HealthRegen:F1}/s", label);
                GUILayout.Label($"燃烧抗性 {s.BurnResistance:P0} · 中毒抗性 {s.PoisonResistance:P0}\n减速 {s.SlowResistance:P0} · 眩晕 {s.StunResistance:P0} · 易伤 {s.MarkResistance:P0}", small);
                GUILayout.Label($"步行 {s.MoveSpeed:F2} m/s · 冲刺 ×{s.SprintMultiplier:F2}\n击退抗性 {s.ImpulseResistance:P0}\n体力 {s.MaxStamina:F0} · 回复 {s.StaminaRegen:F1}/s · 冲刺消耗 {s.SprintCost:F1}/s", small);
                GUILayout.Label($"受击保护 {s.HitInvulnerability:F2}s · 应急治疗 {s.EmergencyHealFraction:P0}\n应急冷却 {s.EmergencyCooldown:F1}s · 净化周期 {s.CleanseInterval:F1}s", small);
                GUILayout.Label($"能量 {s.MaxEnergy:F0} · 回复 {s.EnergyRegen:F1}/s · 消耗 ×{s.EnergyCostScale:F2}", small);
            }
            if (!Embedded) { GUILayout.Space(10); GUILayout.Label("WASD 移动 · Shift 冲刺\nSpace 发射测试弹 · T 连续攻击\nR 重置资源与位置 · Esc 关闭选择面板", small); }
            GUILayout.EndArea();
            GUI.matrix = previous;
            if (storage != null) storage.DrawTestPanel();
            if (includeOverlay) DrawOverlay();
        }
        // Draw the selection overlay after the scene's status panel.
        private void DrawPopup()
        {
            GUI.Box(new Rect(402, 60, 630, 790), GUIContent.none);
            GUILayout.BeginArea(new Rect(418, 75, 598, 758));
            GUILayout.BeginHorizontal(); GUILayout.Label(recipesOpen ? "选择装备配方" : "选择" + EquipmentSlots.Name(openKind.Value) + "配件", title);
            if (GUILayout.Button("关闭", button, GUILayout.Width(60), GUILayout.Height(32))) ClosePopup();
#if UNITY_EDITOR
            RecordControlRect("popup.close");
#endif
            GUILayout.EndHorizontal();
            if (!PopupOpen) { GUILayout.EndArea(); return; }
            GUILayout.Label("搜索名称 / 效果", small); search = GUILayout.TextField(search);
#if UNITY_EDITOR
            RecordControlRect("popup.search");
#endif
            scroll = GUILayout.BeginScrollView(scroll);
            if (recipesOpen && Catalog != null)
            {
                foreach (var recipe in Catalog.Recipes)
                {
                    if (recipe == null || !Matches(recipe.DisplayName, recipe.Description)) continue;
                    if (GUILayout.Button(recipe.DisplayName + "\n" + recipe.Description, button, GUILayout.MinHeight(64))) { SelectRecipe(recipe); break; }
#if UNITY_EDITOR
                    RecordControlRect("recipe." + recipe.RecipeId);
#endif
                }
            }
            else if (openKind.HasValue)
            {
                var kind = openKind.Value;
                bool clear = GUILayout.Button("空配件\n清除此槽位，不添加效果", button, GUILayout.Height(56));
#if UNITY_EDITOR
                RecordControlRect("part.empty");
#endif
                if (clear) { SelectPart(kind, null); }
                else if (Catalog != null) foreach (var part in Catalog.Parts)
                {
                    if (part == null || part.Kind != kind || !Matches(part.DisplayName, part.Description)) continue;
                    if (GUILayout.Button(part.DisplayName + "\n" + part.Description, button, GUILayout.MinHeight(66))) { SelectPart(kind, part); break; }
#if UNITY_EDITOR
                    RecordControlRect("part." + part.PartId);
#endif
                }
            }
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
        public void DrawOverlay()
        {
            if (!PopupOpen) return;
            Styles(); var previous = GUI.matrix; GUI.matrix = Matrix4x4.Scale(Vector3.one * Scale); DrawPopup(); GUI.matrix = previous;
        }
        private bool Matches(string name, string description) => string.IsNullOrWhiteSpace(search)
            || ((name ?? "") + " " + (description ?? "")).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        private void OnDestroy() { if (font != null) Destroy(font); }
    }
}
