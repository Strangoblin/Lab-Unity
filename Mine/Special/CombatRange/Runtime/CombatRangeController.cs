using System.Collections.Generic;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Runtime;
using CelToon.CustomEquipment.Samples;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Definitions;
using CelToon.CustomLauncher.Projectiles;
using CelToon.CustomLauncher.Runtime;
using CelToon.CustomLauncher.Samples;
using CelToon.CustomItem.Samples;
using UnityEngine;

namespace CelToon.CombatRange
{
    [DefaultExecutionOrder(100)]
    public sealed class CombatRangeController : MonoBehaviour
    {
        public EquipmentHost Player;
        public CharacterController Motor;
        public LauncherHost Launcher, Attacker;
        public ProjectilePool PlayerPool, ThreatPool;
        public Transform PlayerMuzzle, ThreatMuzzle;
        public Camera RangeCamera;
        public RangeTarget[] Targets;
        public LauncherWorkbenchUI LauncherWorkbench;
        public EquipmentWorkbenchUI EquipmentWorkbench;
        public ItemRangeWorkbench ItemWorkbench;
        public Renderer PlayerBody;
        public GameObject[] AttachmentVisuals;
        public LauncherRecipeDefinition[] Threats;
        public bool AutoAttack;
        public int ThreatIndex;
        public bool EquipmentTab { get; private set; }
        public bool ItemTab { get; private set; }
        public bool PopupOpen => (LauncherWorkbench != null && LauncherWorkbench.PopupOpen) || (EquipmentWorkbench != null && EquipmentWorkbench.PopupOpen);
        public bool Ready => initialized && Player != null && Player.State?.Plan != null && Launcher != null && Launcher.State?.Plan != null;
        public FireResult LastFireResult { get; private set; }
        public Vector3 AimDirection => Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward;
        public float EffectiveShotCost => Ready ? Launcher.State.Plan.EnergyCost * Player.State.Plan.Stats.EnergyCostScale : 0;
        public float Scale => Mathf.Max(.1f, Mathf.Min(Screen.width / 1440f, Screen.height / 900f));
        private bool initialized, fireRequested, threatRequested, resetRequested, toggleRequested, closeRequested, pickupRequested;
        private Vector3 home;
        private float yaw, pitch, nextAttack, flashUntil;
        private string message = "正在初始化综合靶场";
        private readonly HashSet<KeyCode> keys = new HashSet<KeyCode>();
        private Font font;
        private GUIStyle title, label, small, button;
        private MaterialPropertyBlock properties;
        private Vector2 statusScroll;
#if UNITY_EDITOR
        private readonly Dictionary<string, Rect> controls = new Dictionary<string, Rect>();
        public bool TryGetControlRect(string id, out Rect rect) => controls.TryGetValue(id, out rect);
        private void Record(string id, Vector2 origin)
        {
            if (Event.current.type != EventType.Repaint) return;
            var rect = GUILayoutUtility.GetLastRect();
            var min = GUIUtility.GUIToScreenPoint(rect.min) + origin * (Scale - 1);
            var max = GUIUtility.GUIToScreenPoint(rect.max) + origin * (Scale - 1);
            controls[id] = new Rect(min, max - min);
        }
#endif
        private void Start() { TryInitialize(); }
        public bool TryInitialize()
        {
            if (initialized) return Ready;
            if (Player == null || Motor == null || Launcher == null || Attacker == null || PlayerPool == null || ThreatPool == null
                || PlayerMuzzle == null || ThreatMuzzle == null || LauncherWorkbench == null || EquipmentWorkbench == null
                || RangeCamera == null || Targets == null || Threats == null || Threats.Length == 0) return false;
            LauncherWorkbench.Host = Launcher; EquipmentWorkbench.Host = Player;
            LauncherWorkbench.DrawAutomatically = EquipmentWorkbench.DrawAutomatically = false;
            LauncherWorkbench.Embedded = EquipmentWorkbench.Embedded = true;
            if (Player.State?.Plan == null && !EquipmentWorkbench.SelectRecipe(EquipmentWorkbench.InitialRecipe)) return false;
            if (Launcher.State?.Plan == null && !LauncherWorkbench.SelectRecipe(LauncherWorkbench.InitialRecipe)) return false;
            if (!SelectThreat(ThreatIndex)) return false;
            Player.AutoTick = false; Launcher.BindEnergySource(new EquipmentEnergySource(Player));
            if (ItemWorkbench != null) {
                ItemWorkbench.Launcher = Launcher; ItemWorkbench.Equipment = Player;
                ItemWorkbench.Player = Player.transform; ItemWorkbench.RangeCamera = RangeCamera;
                ItemWorkbench.TryInitialize();
            }
            home = Player.transform.position; properties = new MaterialPropertyBlock();
            Player.Damaged += OnDamaged; initialized = true; message = "已就绪：玩家射击与受击共用同一套装备状态"; return true;
        }
        private void OnDamaged(EquipmentDamageReport report) { flashUntil = Time.time + .15f; }
        public void SetEquipmentTab(bool value)
        { ItemTab = false; EquipmentTab = value; CloseWorkbenchPopups(); }
        public void SetItemTab()
        { ItemTab = true; CloseWorkbenchPopups(); }
        private void CloseWorkbenchPopups()
        { LauncherWorkbench?.ClosePopup(); EquipmentWorkbench?.ClosePopup(); if (Event.current != null) GUI.FocusControl(null); keys.Clear(); fireRequested = false; }
        public bool SelectThreat(int index)
        {
            if (Attacker == null || Threats == null || index < 0 || index >= Threats.Length || Threats[index] == null) return false;
            if (!Attacker.TryEquip(Threats[index].CreateLoadout())) return false;
            ThreatIndex = index; return true;
        }
        public FireResult FirePlayer()
        {
            if (!Ready || Player.State.IsDead) return FireResult.NoPlan;
            PlayerMuzzle.rotation = Quaternion.LookRotation(AimDirection);
            LastFireResult = Launcher.TryFire(PlayerPool, new FireAim(PlayerMuzzle.position, AimDirection, Vector3.up, Player.gameObject.GetInstanceID(), 1), Time.time);
            if (LastFireResult != FireResult.CoolingDown) message = LastFireResult == FireResult.Fired ? "玩家发射成功，已扣除装备能量"
                : LastFireResult == FireResult.InsufficientEnergy ? "人物能量不足，等待回复或重置"
                : LastFireResult == FireResult.EmptyPayload ? "弹丸槽为空，请选择弹丸"
                : LastFireResult == FireResult.CapacityExceeded ? "弹丸池容量不足，本次未扣费" : "发射失败：" + LastFireResult;
            return LastFireResult;
        }
        public FireResult FireThreat()
        {
            if (!Ready || Player.State.IsDead) return FireResult.NoPlan;
            Attacker.State.Recharge(Attacker.State.MaxEnergy);
            var direction = (Player.transform.position + Vector3.up - ThreatMuzzle.position).normalized;
            ThreatMuzzle.rotation = Quaternion.LookRotation(direction);
            var result = Attacker.TryFire(ThreatPool, new FireAim(ThreatMuzzle.position, direction, Vector3.up, Attacker.gameObject.GetInstanceID(), 2), Time.time);
            message = result == FireResult.Fired ? "敌方测试弹：" + Threats[ThreatIndex].DisplayName : "敌方发射：" + result;
            return result;
        }
        public void DirectHit(float amount = 25)
        { if (Ready) Player.ReceiveDamage(new DamageInfo(amount, Player.transform.position + Vector3.up, Vector3.back, 0, 2)); }
        public void ResetRange()
        {
            if (!Ready) return;
            AutoAttack = false; nextAttack = 0; PlayerPool.Clear(); ThreatPool.Clear(); Player.ResetPlayer();
            Motor.enabled = false; Player.transform.SetPositionAndRotation(home, Quaternion.identity); Motor.enabled = true;
            yaw = pitch = 0; keys.Clear();
            foreach (var target in Targets) if (target != null) target.ResetTarget();
            Launcher.State.ResetFiring(); Attacker.State.ResetFiring();
            LauncherWorkbench.ClosePopup(); EquipmentWorkbench.ClosePopup(); message = "已重置玩家、靶标、冷却与弹丸；连续受击已停止";
        }
        public bool BlocksScreenPoint(Vector3 mouse)
        {
            var p = new Vector2(mouse.x / Scale, (Screen.height - mouse.y) / Scale);
            return p.x < 400 || p.x > 1040 || p.y < 65 || p.y > 832 || PopupOpen;
        }
        private bool Held(KeyCode key) => keys.Contains(key) || Input.GetKey(key);
        public void HandleGUIInput(Event input)
        {
            if (input == null || (input.type != EventType.KeyDown && input.type != EventType.KeyUp)) return;
            var key = input.keyCode;
            bool supported = key == KeyCode.W || key == KeyCode.A || key == KeyCode.S || key == KeyCode.D || key == KeyCode.LeftShift
                || key == KeyCode.Space || key == KeyCode.R || key == KeyCode.T || key == KeyCode.H || key == KeyCode.E || key == KeyCode.Escape
                || key == KeyCode.LeftArrow || key == KeyCode.RightArrow || key == KeyCode.UpArrow || key == KeyCode.DownArrow;
            if (!supported) return;
            if (input.type == EventType.KeyUp) { keys.Remove(key); return; }
            if (key == KeyCode.Escape) { closeRequested = true; input.Use(); return; }
            if (PopupOpen) return;
            if (keys.Add(key)) {
                if (key == KeyCode.Space) fireRequested = true;
                if (key == KeyCode.R) resetRequested = true;
                if (key == KeyCode.T) toggleRequested = true;
                if (key == KeyCode.H) threatRequested = true;
                if (key == KeyCode.E) pickupRequested = true;
            }
            input.Use();
        }
        private void OnApplicationFocus(bool focused)
        { if (!focused) { keys.Clear(); fireRequested = threatRequested = resetRequested = toggleRequested = closeRequested = pickupRequested = false; } }
        private void Update()
        {
            if (RangeCamera != null && Screen.width > 0 && Screen.height > 0) {
                float scale = Scale;
                RangeCamera.rect = new Rect(400 * scale / Screen.width, 1 - 900 * scale / Screen.height,
                    640 * scale / Screen.width, 900 * scale / Screen.height);
            }
            if (!TryInitialize()) return;
            if (closeRequested) { LauncherWorkbench.ClosePopup(); EquipmentWorkbench.ClosePopup(); closeRequested = false; }
            bool popup = PopupOpen;
            if (!popup) {
                if (resetRequested) ResetRange();
                if (toggleRequested && !Player.State.IsDead) AutoAttack = !AutoAttack;
                if (threatRequested) FireThreat();
                if (pickupRequested || Input.GetKeyDown(KeyCode.E)) ItemWorkbench?.PickupNearest();
                yaw += ((Held(KeyCode.RightArrow) ? 1 : 0) - (Held(KeyCode.LeftArrow) ? 1 : 0)) * 35 * Time.deltaTime;
                pitch += ((Held(KeyCode.UpArrow) ? 1 : 0) - (Held(KeyCode.DownArrow) ? 1 : 0)) * 25 * Time.deltaTime;
                if (Input.GetMouseButton(1) && !BlocksScreenPoint(Input.mousePosition)) {
                    var ray = RangeCamera.ScreenPointToRay(Input.mousePosition);
                    if (new Plane(Vector3.up, Vector3.up * 1.25f).Raycast(ray, out float distance)) {
                        var direction = (ray.GetPoint(distance) - PlayerMuzzle.position).normalized;
                        yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg; pitch = Mathf.Asin(direction.y) * Mathf.Rad2Deg;
                    }
                }
                yaw = Mathf.Clamp(yaw, -85, 85); pitch = Mathf.Clamp(pitch, -20, 45);
                if (ItemTab && Input.GetMouseButtonDown(0) && !BlocksScreenPoint(Input.mousePosition)) ItemWorkbench?.SelectWorldItem(Input.mousePosition);
                if (fireRequested || Held(KeyCode.Space) || (!ItemTab && Input.GetMouseButton(0) && !BlocksScreenPoint(Input.mousePosition))) FirePlayer();
            }
            fireRequested = threatRequested = resetRequested = toggleRequested = pickupRequested = false;
            var move = popup ? Vector3.zero : new Vector3((Held(KeyCode.D) ? 1 : 0) - (Held(KeyCode.A) ? 1 : 0), 0,
                (Held(KeyCode.W) ? 1 : 0) - (Held(KeyCode.S) ? 1 : 0)).normalized;
            Player.Tick(Time.deltaTime, move.sqrMagnitude > 0 && Held(KeyCode.LeftShift));
            if (Motor.enabled) Motor.Move((move * Player.State.MoveSpeed + Player.ImpulseVelocity + Vector3.down * 3) * Time.deltaTime);
            if (Player.State.IsDead) AutoAttack = false;
            if (AutoAttack && !popup && Time.time >= nextAttack) { FireThreat(); nextAttack = Time.time + 1.2f; }
            PlayerMuzzle.rotation = Quaternion.LookRotation(AimDirection);
            if (AttachmentVisuals != null) for (int i = 0; i < AttachmentVisuals.Length && i < EquipmentSlots.Count; i++)
                if (AttachmentVisuals[i] != null) AttachmentVisuals[i].SetActive(ItemWorkbench != null && ItemWorkbench.ItemMode
                    ? ItemWorkbench.HasEquipmentCore(EquipmentSlots.Id(EquipmentSlots.At(i))) : EquipmentWorkbench.CurrentPart(EquipmentSlots.At(i)) != null);
            if (PlayerBody != null) {
                var color = Player.State.IsDead ? Color.gray : Time.time < flashUntil ? Color.white
                    : Player.State.StatusRemaining(StatusKind.Burn) > 0 ? new Color(1, .3f, .1f)
                    : Player.State.StatusRemaining(StatusKind.Poison) > 0 ? Color.green : new Color(.35f, .7f, .8f);
                properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color); PlayerBody.SetPropertyBlock(properties);
            }
        }
        private void Styles()
        {
            if (label != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 16);
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16, wordWrap = true };
            small = new GUIStyle(label) { fontSize = 13 }; title = new GUIStyle(label) { fontSize = 21, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 15, wordWrap = true };
        }
        private void OnGUI()
        {
            HandleGUIInput(Event.current); Styles();
            if (ItemTab) ItemWorkbench?.DrawPanel();
            else if (ItemWorkbench != null && ItemWorkbench.ItemMode) ItemWorkbench.DrawLockedPanel();
            else if (EquipmentTab) EquipmentWorkbench?.DrawPanel(false); else LauncherWorkbench?.DrawPanel(false);
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(Vector3.one * Scale);
            GUILayout.BeginArea(new Rect(26, 18, 348, 42)); GUILayout.BeginHorizontal();
            if (GUILayout.Button(!ItemTab && !EquipmentTab ? "● 发射器" : "发射器", button, GUILayout.Height(36))) SetEquipmentTab(false);
#if UNITY_EDITOR
            Record("tab.launcher", new Vector2(26, 18));
#endif
            if (GUILayout.Button(!ItemTab && EquipmentTab ? "● 装备" : "装备", button, GUILayout.Height(36))) SetEquipmentTab(true);
#if UNITY_EDITOR
            Record("tab.equipment", new Vector2(26, 18));
#endif
            if (GUILayout.Button(ItemTab ? "● 物品" : "物品", button, GUILayout.Height(36))) SetItemTab();
#if UNITY_EDITOR
            Record("tab.item", new Vector2(26, 18));
#endif
            GUILayout.EndHorizontal(); GUILayout.EndArea();
            GUI.Label(new Rect(410, 18, 620, 40), "综合靶场  /  射击 · 受击 · 双系统装配", title);
            GUI.Box(new Rect(1050, 12, 378, 874), GUIContent.none);
            GUILayout.BeginArea(new Rect(1066, 26, 346, 846));
            statusScroll = GUILayout.BeginScrollView(statusScroll);
            if (!Ready) GUILayout.Label("场景绑定或配方缺失，请重建或检查综合靶场", label);
            else DrawDashboard();
            GUILayout.EndScrollView(); GUILayout.EndArea();
            GUI.Label(new Rect(410, 840, 620, 48), "WASD 移动 · Shift 冲刺 · 方向键 / 右键瞄准 · E 拾取\nSpace / 左键射击 · H 受击弹 · T 连续受击 · R 重置 · Esc 关闭", small);
            if (Ready && RangeCamera != null) {
                var p = RangeCamera.WorldToScreenPoint(PlayerMuzzle.position + AimDirection * 17);
                if (p.z > 0) GUI.Label(new Rect(p.x / Scale - 8, (Screen.height - p.y) / Scale - 12, 28, 28), "+", title);
            }
            GUI.matrix = old;
            if (!ItemTab && (ItemWorkbench == null || !ItemWorkbench.ItemMode)) {
                if (EquipmentTab) EquipmentWorkbench?.DrawOverlay(); else LauncherWorkbench?.DrawOverlay();
            }
            ItemWorkbench?.DrawWorldLabels();
        }
        private void DrawDashboard()
        {
            var state = Player.State; var stats = state.Plan.Stats;
            GUILayout.Label("玩家状态", title);
            Bar("生命", state.Health, stats.MaxHealth, new Color(.4f, .85f, .45f));
            Bar("护盾", state.Shield, stats.MaxShield, new Color(.3f, .65f, 1));
            Bar("体力", state.Stamina, stats.MaxStamina, new Color(1, .75f, .3f));
            Bar("能量", state.Energy, stats.MaxEnergy, new Color(.75f, .45f, 1));
            GUILayout.Label(state.IsDead ? "已倒下，按 R 重置" : $"移速 {state.MoveSpeed:F2} m/s · {state.StatusSummary}", small);
            GUILayout.Label($"射击：基础费用 {Launcher.State.Plan.EnergyCost:F1} × {stats.EnergyCostScale:F2} = {EffectiveShotCost:F1}\n能量回复 {stats.EnergyRegen:F1}/s · 已开火 {Launcher.State.ShotCount}", small);
            if (GUILayout.Button("玩家射击 [Space]", button, GUILayout.Height(32))) FirePlayer();
#if UNITY_EDITOR
            Record("player.fire", new Vector2(1066, 26));
#endif
            if (GUILayout.Button("重置玩家与靶标 [R]", button, GUILayout.Height(32))) ResetRange();
#if UNITY_EDITOR
            Record("range.reset", new Vector2(1066, 26));
#endif
            GUILayout.Space(8); GUILayout.Label("受击测试", title); GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", button, GUILayout.Width(36))) SelectThreat((ThreatIndex + Threats.Length - 1) % Threats.Length);
            GUILayout.Label(Threats[ThreatIndex].DisplayName, label);
            if (GUILayout.Button("▶", button, GUILayout.Width(36))) SelectThreat((ThreatIndex + 1) % Threats.Length);
            GUILayout.EndHorizontal(); GUILayout.Label(Threats[ThreatIndex].Description, small);
            if (GUILayout.Button("发射敌方测试弹 [H]", button, GUILayout.Height(32))) FireThreat();
#if UNITY_EDITOR
            Record("threat.fire", new Vector2(1066, 26));
#endif
            if (GUILayout.Button(AutoAttack ? "停止连续受击 [T]" : "开始连续受击 [T]", button, GUILayout.Height(32))) AutoAttack = !AutoAttack;
            if (GUILayout.Button("直接承受 25 点伤害", button, GUILayout.Height(30))) DirectHit();
#if UNITY_EDITOR
            Record("threat.hit", new Vector2(1066, 26));
#endif
            var last = state.LastDamage;
            GUILayout.Label($"最近受击 {last.Incoming:F1} · 减免 {last.Mitigated:F1}\n护盾损失 {last.ShieldLost:F1} · 生命损失 {last.HealthLost:F1}\n累计减免 {state.TotalMitigated:F1} · 命中 {state.Hits}\n应急触发 {state.EmergencyActivations}", small);
            GUILayout.Space(8); GUILayout.Label("射击记录", title);
            foreach (var target in Targets) if (target != null) GUILayout.Label($"{target.name} · 伤害 {target.TotalDamage:F1}\n命中 {target.Hits} · 击倒 {target.Knockdowns} · {target.StatusSummary}", small);
            GUILayout.Label($"玩家弹丸 {PlayerPool.ActiveCount}/{PlayerPool.Capacity} · 分裂 {PlayerPool.TriggerCount}\n敌方弹丸 {ThreatPool.ActiveCount}/{ThreatPool.Capacity}\n被预算阻止 {PlayerPool.RejectedTriggers}", small);
            GUILayout.Label(message, small);
        }
        private void Bar(string name, float value, float max, Color color)
        {
            GUILayout.Label($"{name}  {value:F1} / {max:F0}", label);
            var rect = GUILayoutUtility.GetRect(310, 10); GUI.Box(rect, GUIContent.none);
            var old = GUI.color; GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x + 1, rect.y + 1, (rect.width - 2) * (max > 0 ? Mathf.Clamp01(value / max) : 0), rect.height - 2), Texture2D.whiteTexture); GUI.color = old;
        }
        private void OnDestroy() { if (Player != null) Player.Damaged -= OnDamaged; if (font != null) Destroy(font); }
    }
}
