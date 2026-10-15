using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Runtime;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Definitions;
using CelToon.CustomLauncher.Projectiles;
using CelToon.CustomLauncher.Runtime;
using System.Collections.Generic;
using UnityEngine;

namespace CelToon.CustomEquipment.Samples
{
    [DefaultExecutionOrder(100)]
    public sealed class StorageController : MonoBehaviour
    {
        public EquipmentHost Player;
        public CharacterController Motor;
        public EquipmentWorkbenchUI Workbench;
        public LauncherHost Attacker;
        public ProjectilePool Pool;
        public Transform Muzzle;
        public Camera StorageCamera;
        public Renderer PlayerBody;
        public GameObject[] AttachmentVisuals;
        public LauncherRecipeDefinition[] Threats;
        public bool AutoAttack;
        public int ThreatIndex;
        private Vector3 home;
        private float nextAttack, flashUntil;
        private MaterialPropertyBlock properties;
        private Font font;
        private GUIStyle label, small, title, button;
        private string message = "已就绪：选择攻击类型，发射测试弹";
        private bool initialized;
        private readonly HashSet<KeyCode> guiKeys = new HashSet<KeyCode>();
        private bool resetRequested, attackRequested, toggleAttackRequested, closeRequested;
        public void HandleGUIInput(Event input)
        {
            if (input == null || (input.type != EventType.KeyDown && input.type != EventType.KeyUp)) return;
            var key = input.keyCode;
            bool supported = key == KeyCode.W || key == KeyCode.A || key == KeyCode.S || key == KeyCode.D || key == KeyCode.LeftShift
                || key == KeyCode.Space || key == KeyCode.R || key == KeyCode.T || key == KeyCode.Escape;
            if (!supported) return;
            if (input.type == EventType.KeyUp) { guiKeys.Remove(key); return; }
            if (key == KeyCode.Escape) { closeRequested = true; input.Use(); return; }
            if (!Ready || Workbench == null || Workbench.PopupOpen) return;
            if (guiKeys.Add(key))
            {
                if (key == KeyCode.R) resetRequested = true;
                if (key == KeyCode.T) toggleAttackRequested = true;
                if (key == KeyCode.Space) attackRequested = true;
            }
            // Do not let Space also activate the last focused IMGUI button.
            input.Use();
        }
        private bool Held(KeyCode key) => Input.GetKey(key) || guiKeys.Contains(key);
        private void OnApplicationFocus(bool focused)
        { if (!focused) { guiKeys.Clear(); resetRequested = attackRequested = toggleAttackRequested = closeRequested = false; } }
        public bool Ready => Player?.State?.Plan != null && Attacker?.State?.Plan != null;
        private void Start()
        {
            TryInitialize();
        }
        // Recover object links within this Storage only. Never bind another loaded scene's player.
        public void ResolveSceneBindings()
        {
            if (Player == null) Player = GetComponentInChildren<EquipmentHost>(true);
            if (Workbench == null) Workbench = GetComponent<EquipmentWorkbenchUI>();
            if (Attacker == null) Attacker = GetComponentInChildren<LauncherHost>(true);
            if (Pool == null) Pool = GetComponent<ProjectilePool>();
            if (StorageCamera == null) StorageCamera = GetComponentInChildren<Camera>(true);
            if (Player != null)
            {
                if (Motor == null) Motor = Player.GetComponent<CharacterController>();
                if (PlayerBody == null) PlayerBody = Player.transform.Find("PlayerBody")?.GetComponent<Renderer>();
                if (AttachmentVisuals == null || AttachmentVisuals.Length != EquipmentSlots.Count)
                    AttachmentVisuals = new GameObject[EquipmentSlots.Count];
                var legacyVisuals = new[] { "Vitality", "Shield", "Mobility", "Reaction" };
                var previousVisuals = new[] { "DefenseCore", "ShieldGenerator", "MobilityDrive", "EmergencyDevice" };
                for (int i = 0; i < AttachmentVisuals.Length; i++) {
                    var visual = AttachmentVisuals[i] != null ? AttachmentVisuals[i].transform : Player.transform.Find("Attachment_" + EquipmentSlots.At(i))
                        ?? Player.transform.Find("Attachment_" + previousVisuals[i]) ?? Player.transform.Find("Attachment_" + legacyVisuals[i]);
                    if (visual != null) { visual.name = "Attachment_" + EquipmentSlots.At(i); AttachmentVisuals[i] = visual.gameObject; }
                }
                // Retire only the old sample markers. Keep the player's body and scene geometry.
                foreach (var retired in new[] { "Armor", "Resistance", "Recovery", "Utility" })
                    Player.transform.Find("Attachment_" + retired)?.gameObject.SetActive(false);
            }
            if (Muzzle == null && Attacker != null) Muzzle = Attacker.transform.Find("Muzzle");
            if (Workbench != null && Workbench.Host == null) Workbench.Host = Player;
        }
        public bool TryInitialize()
        {
            if (initialized && Ready) return true;
            ResolveSceneBindings();
            if (Player == null || Motor == null || Workbench == null || Attacker == null || Pool == null || Muzzle == null)
            { message = "Storage 对象绑定不完整，请在编辑器菜单修复 Storage 绑定"; return false; }
            if (Player.State?.Plan == null && !Workbench.SelectRecipe(Workbench.InitialRecipe))
            { message = "装备资源未绑定，请使用 CelToon/Custom Equipment/Repair Storage Bindings"; return false; }
            if (!SelectThreat(ThreatIndex))
            { message = "测试攻击未绑定，请在编辑器菜单修复 Storage 绑定"; return false; }
            if (!initialized)
            {
                home = Player.transform.position; properties = new MaterialPropertyBlock();
                Player.Damaged -= OnDamaged; Player.Damaged += OnDamaged;
            }
            Player.AutoTick = false; initialized = true; return true;
        }
        private void OnDamaged(EquipmentDamageReport report) { flashUntil = Time.time + .15f; }
        public bool SelectThreat(int index)
        {
            if (Threats == null || index < 0 || index >= Threats.Length || Threats[index] == null) return false;
            if (!Attacker.TryEquip(Threats[index].CreateLoadout())) return false;
            ThreatIndex = index; return true;
        }
        public FireResult Fire()
        {
            if (!Ready) return FireResult.NoPlan;
            if (Player.State.IsDead) { message = "玩家已倒下，按 R 重新测试"; return FireResult.NoPlan; }
            Attacker.State.Recharge(Attacker.State.MaxEnergy);
            var direction = (Player.transform.position + Vector3.up - Muzzle.position).normalized;
            Muzzle.rotation = Quaternion.LookRotation(direction);
            var result = Attacker.TryFire(Pool, new FireAim(Muzzle.position, direction, Vector3.up, Attacker.gameObject.GetInstanceID(), 2), Time.time);
            message = result == FireResult.Fired ? "已发射：" + Threats[ThreatIndex].DisplayName : "发射结果：" + result;
            return result;
        }
        public void ResetStorage()
        {
            AutoAttack = false; nextAttack = 0; Pool.Clear(); Player.ResetPlayer();
            Motor.enabled = false; Player.transform.position = home; Player.transform.rotation = Quaternion.identity; Motor.enabled = true;
            message = "资源与统计已重置，连续攻击已停止";
        }
        public void DirectHit(float amount = 25)
        { Player.ReceiveDamage(new DamageInfo(amount, Player.transform.position + Vector3.up, Vector3.back, 0, 2)); }
        public bool SpendTestEnergy(float baseCost = 25)
        {
            bool success = Ready && Player.State.TrySpendEnergy(baseCost);
            message = success ? "已消耗玩家能量，观察回复与消耗倍率" : "能量不足或玩家已倒下"; return success;
        }
        private void Update()
        {
            if (!TryInitialize()) return;
            if (closeRequested) { Workbench.ClosePopup(); closeRequested = false; }
            bool popup = Workbench.PopupOpen;
            if (!popup)
            {
                if (resetRequested) ResetStorage();
                if (toggleAttackRequested) AutoAttack = !AutoAttack;
                if (attackRequested) Fire();
            }
            resetRequested = attackRequested = toggleAttackRequested = false;
            var movement = popup ? Vector3.zero : new Vector3((Held(KeyCode.D) ? 1 : 0) - (Held(KeyCode.A) ? 1 : 0), 0,
                (Held(KeyCode.W) ? 1 : 0) - (Held(KeyCode.S) ? 1 : 0)).normalized;
            Player.Tick(Time.deltaTime, movement.sqrMagnitude > 0 && Held(KeyCode.LeftShift));
            var velocity = movement * Player.State.MoveSpeed + Player.ImpulseVelocity;
            if (Motor.enabled) Motor.Move((velocity + Vector3.down * 3) * Time.deltaTime);
            if (movement.sqrMagnitude > 0 && Player.State.MovementFactor > 0)
                Player.transform.rotation = Quaternion.Slerp(Player.transform.rotation, Quaternion.LookRotation(movement), Time.deltaTime * 10);
            if (AutoAttack && !Player.State.IsDead && Time.time >= nextAttack)
            { Fire(); nextAttack = Time.time + 1.2f; }
            if (AttachmentVisuals != null) for (int i = 0; i < AttachmentVisuals.Length && i < EquipmentSlots.Count; i++)
                if (AttachmentVisuals[i] != null) AttachmentVisuals[i].SetActive(Workbench.CurrentPart(EquipmentSlots.At(i)) != null);
            if (PlayerBody != null)
            {
                var color = Player.State.IsDead ? Color.gray : Time.time < flashUntil ? Color.white
                    : Player.State.StatusRemaining(StatusKind.Burn) > 0 ? new Color(1, .3f, .1f)
                    : Player.State.StatusRemaining(StatusKind.Poison) > 0 ? new Color(.4f, .9f, .25f)
                    : Player.State.IsInvulnerable ? Color.cyan : new Color(.35f, .7f, .8f);
                properties.SetColor("_Color", color); properties.SetColor("_BaseColor", color); PlayerBody.SetPropertyBlock(properties);
            }
        }
        public void DrawTestPanel()
        {
            if (Workbench == null) return;
            if (label == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 16);
                label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16, wordWrap = true };
                small = new GUIStyle(label) { fontSize = 13 };
                title = new GUIStyle(label) { fontSize = 22, fontStyle = FontStyle.Bold };
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 15, wordWrap = true };
            }
            var previous = GUI.matrix; GUI.matrix = Matrix4x4.Scale(Vector3.one * Workbench.Scale);
            GUI.Box(new Rect(1050, 12, 378, 874), GUIContent.none);
            GUILayout.BeginArea(new Rect(1066, 26, 346, 846));
            GUILayout.Label("受击测试台", title);
            if (Ready)
            {
                var state = Player.State; var s = state.Plan.Stats;
                Bar("生命", state.Health, s.MaxHealth, new Color(.4f, .85f, .45f));
                Bar("护盾", state.Shield, s.MaxShield, new Color(.3f, .65f, 1));
                Bar("体力", state.Stamina, s.MaxStamina, new Color(1, .75f, .3f));
                Bar("能量", state.Energy, s.MaxEnergy, new Color(.75f, .45f, 1));
                if (GUILayout.Button("消耗 25 基础能量（属性测试）", button, GUILayout.Height(30))) SpendTestEnergy();
#if UNITY_EDITOR
                Workbench.RecordControlRect("test.energy");
#endif
                GUILayout.Label(state.IsDead ? "玩家倒下 · R 重新测试" : $"当前移速 {state.MoveSpeed:F2} m/s" + (state.Sprinting ? " · 冲刺" : ""), label);
                GUILayout.Label(state.StatusSummary, small); GUILayout.Space(12);
                GUILayout.Label("攻击配置", title);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("◀", button, GUILayout.Width(36))) SelectThreat((ThreatIndex + Threats.Length - 1) % Threats.Length);
                GUILayout.Label(Threats[ThreatIndex].DisplayName, label);
                if (GUILayout.Button("▶", button, GUILayout.Width(36))) SelectThreat((ThreatIndex + 1) % Threats.Length);
                GUILayout.EndHorizontal();
                GUILayout.Label(Threats[ThreatIndex].Description, small);
                if (GUILayout.Button("发射真实测试弹 [Space]", button, GUILayout.Height(36))) Fire();
#if UNITY_EDITOR
                Workbench.RecordControlRect("test.fire");
#endif
                if (GUILayout.Button(AutoAttack ? "停止连续攻击 [T]" : "开始连续攻击 [T]", button, GUILayout.Height(36))) AutoAttack = !AutoAttack;
                if (GUILayout.Button("立即承受 25 点直接伤害", button, GUILayout.Height(34))) DirectHit();
#if UNITY_EDITOR
                Workbench.RecordControlRect("test.hit");
#endif
                if (GUILayout.Button("重置资源、位置与统计 [R]", button, GUILayout.Height(36))) ResetStorage();
                GUILayout.Space(12); GUILayout.Label("受击记录", title);
                var last = state.LastDamage;
                GUILayout.Label($"最近伤害 {last.Incoming:F1}\n减免 {last.Mitigated:F1} · 护盾吸收 {last.ShieldLost:F1}\n生命损失 {last.HealthLost:F1}", label);
                GUILayout.Label($"累计输入 {state.TotalIncoming:F1} · 减免 {state.TotalMitigated:F1}\n累计护盾损失 {state.TotalShieldLost:F1}\n累计生命损失 {state.TotalHealthLost:F1}\n直接命中 {state.Hits} · 应急触发 {state.EmergencyActivations}\n在场弹丸 {Pool.ActiveCount}/{Pool.Capacity}", small, GUILayout.MinHeight(100));
                GUILayout.Space(8); GUILayout.Label(message, small);
                GUILayout.Label("测试弹由敌方炮台发射。能量按钮测试玩家资源。\n换装不补资源；回复或 R 可补充。", small);
            }
            else GUILayout.Label(message, label);
            GUILayout.EndArea();
            GUI.Label(new Rect(412, 18, 620, 35), "STORAGE  /  装备仓库与防御测试区", title);
            GUI.matrix = previous;
        }
        private void Bar(string name, float value, float max, Color color)
        {
            GUILayout.Label($"{name}  {value:F1} / {max:F0}", label);
            var rect = GUILayoutUtility.GetRect(340, 13); GUI.Box(rect, GUIContent.none);
            var previous = GUI.color; GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x + 1, rect.y + 1, (rect.width - 2) * (max > 0 ? Mathf.Clamp01(value / max) : 0), rect.height - 2), Texture2D.whiteTexture);
            GUI.color = previous;
        }
        private void OnDestroy() { if (Player != null) Player.Damaged -= OnDamaged; if (font != null) Destroy(font); }
    }
}
