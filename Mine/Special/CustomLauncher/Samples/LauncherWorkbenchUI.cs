using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Definitions;
using CelToon.CustomLauncher.Runtime;
using UnityEngine;

namespace CelToon.CustomLauncher.Samples
{
    // Reusable panel: the range's input/targets are not dependencies of the attachment editor.
    public sealed class LauncherWorkbenchUI : MonoBehaviour
    {
        public LauncherHost Host;
        public PartCatalog Catalog;
        public LauncherRecipeDefinition InitialRecipe;
        public bool DrawAutomatically = true;
        public bool Embedded;
        private PartKind? openKind;
        private bool recipesOpen;
        private string search = "", message = "点击配件格更换配件";
        private Vector2 scroll;
        private Font font;
        private GUIStyle label, title, button, small;
        private static readonly string[] names = { "弹丸", "弹丸分布", "飞行轨迹", "伤害属性", "碰撞响应", "触发载荷", "属性修饰", "射击控制" };
        public bool PopupOpen => openKind.HasValue || recipesOpen;
        public PartKind? OpenCategory => openKind;
        public string Message => message;
        public static string CategoryName(PartKind kind) => names[(int)kind];
        private void Start() { if (Host != null && InitialRecipe != null) SelectRecipe(InitialRecipe); }
        public void OpenSlot(PartKind kind) { if (!LauncherSlots.Supported(kind)) return; openKind = kind; recipesOpen = false; search = ""; scroll = Vector2.zero; }
        public void ClosePopup() { openKind = null; recipesOpen = false; }
        public bool SelectRecipe(LauncherRecipeDefinition recipe)
        {
            if (Host == null || recipe == null) return false;
            bool applied = Host.TryEquip(recipe.CreateLoadout());
            message = applied ? "已装配配方：" + recipe.DisplayName : Host.LastCompilation.Diagnostics[0].Message;
            if (applied) ClosePopup(); return applied;
        }
        public bool SelectPart(PartKind kind, PartDefinition part)
        {
            if (Host == null || Host.Loadout == null || !LauncherSlots.Supported(kind)) return false;
            if (part != null && part.Kind != kind) { message = "该配件不属于此类别"; return false; }
            bool applied = Host.TryEquip(Host.Loadout.WithPart(kind, part));
            message = applied ? CategoryName(kind) + "：" + (part != null ? part.DisplayName : "空配件") : Host.LastCompilation.Diagnostics[0].Message;
            if (applied) ClosePopup(); return applied;
        }
        public PartDefinition CurrentPart(PartKind kind) => Host?.Loadout?.Find(kind)?.Part;
        public float Scale => Mathf.Max(.1f, Mathf.Min(Screen.width / 1440f, Screen.height / 900f));
#if UNITY_EDITOR
        private readonly System.Collections.Generic.Dictionary<string, Rect> controlRects = new System.Collections.Generic.Dictionary<string, Rect>();
        public bool TryGetControlRect(string id, out Rect rect) => controlRects.TryGetValue(id, out rect);
        private void RecordControlRect(string id)
        {
            if (Event.current.type != EventType.Repaint) return;
            var rect = GUILayoutUtility.GetLastRect();
            var origin = id.StartsWith("slot.", System.StringComparison.Ordinal) || id == "recipe.menu"
                ? new Vector2(26, Embedded ? 74 : 24) : new Vector2(418, 45);
            var min = GUIUtility.GUIToScreenPoint(rect.min) + origin * (Scale - 1);
            var max = GUIUtility.GUIToScreenPoint(rect.max) + origin * (Scale - 1);
            controlRects[id] = new Rect(min, max - min);
        }
#endif
        public bool BlocksScreenPoint(Vector3 mouse)
        {
            var position = new Vector2(mouse.x / Scale, (Screen.height - mouse.y) / Scale);
            return new Rect(12, 12, 376, 874).Contains(position) || (PopupOpen && new Rect(402, 30, 440, 820).Contains(position));
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
            Styles(); var previous = GUI.matrix; GUI.matrix = Matrix4x4.Scale(Vector3.one * Scale);
            GUI.Box(new Rect(12, 12, 376, 874), GUIContent.none);
            GUILayout.BeginArea(new Rect(26, Embedded ? 74 : 24, 348, Embedded ? 790 : 625));
            GUILayout.Label("自定义发射器 / RANGE", title);
            GUILayout.Label("每类一个槽位 · 全部开放 · 固定执行顺序", small);
            if (GUILayout.Button("配方：" + (Host?.Loadout?.Recipe != null ? Host.Loadout.Recipe.DisplayName : "未选择") + "  ▾", button, GUILayout.Height(38)))
            { recipesOpen = true; openKind = null; search = ""; scroll = Vector2.zero; }
#if UNITY_EDITOR
            RecordControlRect("recipe.menu");
#endif
            GUILayout.Space(8);
            for (int row = 0; row < 4; row++)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < 2; column++)
                {
                    var kind = (PartKind)(row * 2 + column); var part = CurrentPart(kind);
                    if (GUILayout.Button(CategoryName(kind) + "\n" + (part != null ? part.DisplayName : "空配件"), button, GUILayout.Width(170), GUILayout.Height(64))) OpenSlot(kind);
#if UNITY_EDITOR
                    RecordControlRect("slot." + kind);
#endif
                }
                GUILayout.EndHorizontal(); GUILayout.Space(4);
            }
            GUILayout.Label(message, small);
            if (Host?.State?.Plan != null)
            {
                var plan = Host.State.Plan;
                GUILayout.Label(plan.HasProjectile ? "输出：" + plan.Count + " 发 / 每发伤害 " + plan.Projectile.Damage.ToString("F1") : "弹丸槽为空：当前无法开火", label);
                GUILayout.Label("冷却 " + plan.Cooldown.ToString("F2") + " s · 能量消耗 " + plan.EnergyCost.ToString("F1"), small);
                GUILayout.Label("能量 " + Host.State.Energy.ToString("F1") + " / " + Host.State.MaxEnergy + " · 已开火 " + Host.State.ShotCount, small);
            }
            GUILayout.EndArea();
            if (includeOverlay && PopupOpen) DrawPopup();
            GUI.matrix = previous;
        }
        public void DrawOverlay()
        {
            if (!PopupOpen) return;
            Styles(); var previous = GUI.matrix; GUI.matrix = Matrix4x4.Scale(Vector3.one * Scale);
            DrawPopup(); GUI.matrix = previous;
        }
        private void DrawPopup()
        {
            GUI.Box(new Rect(402, 30, 440, 820), GUIContent.none);
            GUILayout.BeginArea(new Rect(418, 45, 408, 790));
            GUILayout.BeginHorizontal(); GUILayout.Label(recipesOpen ? "选择配方" : "选择" + CategoryName(openKind.Value) + "配件", title);
            if (GUILayout.Button("关闭", button, GUILayout.Width(58), GUILayout.Height(30))) ClosePopup();
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
                    if (GUILayout.Button(recipe.DisplayName + "\n" + recipe.Description, button, GUILayout.MinHeight(68))) { SelectRecipe(recipe); break; }
#if UNITY_EDITOR
                    RecordControlRect("recipe." + recipe.RecipeId);
#endif
                }
            }
            else if (openKind.HasValue)
            {
                var kind = openKind.Value;
                if (GUILayout.Button("空配件\n清除此槽位，不添加任何效果", button, GUILayout.Height(60))) SelectPart(kind, null);
#if UNITY_EDITOR
                RecordControlRect("part.empty");
#endif
                if (Catalog != null) foreach (var part in Catalog.Parts)
                {
                    if (part == null || part.Kind != kind || !Matches(part.DisplayName, part.Description)) continue;
                    if (GUILayout.Button(part.DisplayName + "\n" + part.Description, button, GUILayout.MinHeight(68))) { SelectPart(kind, part); break; }
#if UNITY_EDITOR
                    RecordControlRect("part." + part.PartId);
#endif
                }
            }
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
        private bool Matches(string name, string description) => string.IsNullOrWhiteSpace(search)
            || ((name ?? "") + " " + (description ?? "")).IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        private void OnDestroy() { if (font != null) Destroy(font); }
    }
}
