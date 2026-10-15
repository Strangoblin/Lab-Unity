using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Projectiles;
using CelToon.CustomLauncher.Runtime;
using UnityEngine;

namespace CelToon.CustomLauncher.Samples
{
    public sealed class RangeController : MonoBehaviour
    {
        public LauncherHost Launcher; public ProjectilePool Pool; public Transform Muzzle;
        public Camera RangeCamera; public RangeTarget[] Targets; public LauncherWorkbenchUI Workbench;
        private float yaw, pitch; private string status = "就绪";
        private GUIStyle text, button; private Font font;
        public Vector3 AimDirection => Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
        public FireResult LastFireResult { get; private set; }
        public bool Ready => Launcher != null && Launcher.State?.Plan != null;
        public FireResult Fire()
        {
            if (!Ready) return FireResult.NoPlan;
            LastFireResult = Launcher.TryFire(Pool, new FireAim(Muzzle.position, AimDirection, Vector3.up, Launcher.gameObject.GetInstanceID(), 1), Time.time);
            if (LastFireResult != FireResult.CoolingDown)
            {
                switch (LastFireResult)
                {
                    case FireResult.Fired: status = "已发射"; break;
                    case FireResult.EmptyPayload: status = "弹丸槽为空，请先选择弹丸"; break;
                    case FireResult.InsufficientEnergy: status = "能量不足，按 R 补充"; break;
                    case FireResult.CapacityExceeded: status = "弹丸容量不足，等待回收后重试"; break;
                    default: status = "无法发射，请检查装配与枪口"; break;
                }
            }
            return LastFireResult;
        }
        private void Update()
        {
            if (!Ready) return;
            if (Workbench != null && Workbench.PopupOpen)
            { if (Input.GetKeyDown(KeyCode.Escape)) Workbench.ClosePopup(); return; }
            bool overPanel = Workbench != null && Workbench.BlocksScreenPoint(Input.mousePosition);
            if (Input.GetKey(KeyCode.LeftArrow)) yaw -= 35f * Time.deltaTime;
            if (Input.GetKey(KeyCode.RightArrow)) yaw += 35f * Time.deltaTime;
            if (Input.GetKey(KeyCode.UpArrow)) pitch += 25f * Time.deltaTime;
            if (Input.GetKey(KeyCode.DownArrow)) pitch -= 25f * Time.deltaTime;
            if (Input.GetMouseButton(1) && !overPanel && RangeCamera != null)
            {
                var ray = RangeCamera.ScreenPointToRay(Input.mousePosition);
                if (new Plane(Vector3.forward, new Vector3(0f, 0f, 18f)).Raycast(ray, out float distance))
                {
                    var direction = (ray.GetPoint(distance) - Muzzle.position).normalized;
                    yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg; pitch = Mathf.Asin(direction.y) * Mathf.Rad2Deg;
                }
            }
            yaw = Mathf.Clamp(yaw, -70f, 70f); pitch = Mathf.Clamp(pitch, -15f, 45f);
            Muzzle.rotation = Quaternion.LookRotation(AimDirection, Vector3.up);
            if (Input.GetKey(KeyCode.Space) || (!overPanel && Input.GetMouseButton(0))) Fire();
            if (Input.GetKeyDown(KeyCode.R)) ResetRange();
            if (Input.GetKeyDown(KeyCode.Escape)) Workbench?.ClosePopup();
        }
        public void ResetRange()
        {
            if (Launcher?.State != null) Launcher.State.Recharge(Launcher.State.MaxEnergy);
            Pool?.Clear(); if (Targets != null) foreach (var target in Targets) if (target != null) target.ResetTarget();
            status = "靶标已重置，能量已补满";
        }
        private void OnGUI()
        {
            if (Workbench == null) return;
            if (text == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 15);
                text = new GUIStyle(GUI.skin.label) { font = font, fontSize = 14, wordWrap = true };
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 15 };
            }
            var previous = GUI.matrix; GUI.matrix = Matrix4x4.Scale(Vector3.one * Workbench.Scale);
            GUILayout.BeginArea(new Rect(26, 628, 348, 245));
            if (GUILayout.Button("开火 [Space / 左键]", button, GUILayout.Height(30))) Fire();
            if (GUILayout.Button("重置靶标 + 补满能量 [R]", button, GUILayout.Height(30))) ResetRange();
            GUILayout.Label("方向键 / 右键瞄准 · Esc 关闭选择面板", text);
            if (Pool != null) GUILayout.Label("弹丸 " + Pool.ActiveCount + "/" + Pool.Capacity + " · 分裂事件 " + Pool.TriggerCount + " · 被预算阻止 " + Pool.RejectedTriggers, text);
            GUILayout.Label("状态：" + status, text);
            if (Targets != null) foreach (var target in Targets) if (target != null)
                GUILayout.Label(target.name + " · 伤害 " + target.TotalDamage.ToString("F1") + " · " + target.StatusSummary, text);
            GUILayout.EndArea();
            if (Ready && RangeCamera != null)
            {
                var projected = RangeCamera.WorldToScreenPoint(Muzzle.position + AimDirection * 17f);
                if (projected.z > 0) GUI.Label(new Rect(projected.x / Workbench.Scale - 7, (Screen.height - projected.y) / Workbench.Scale - 12, 28, 28), "+", text);
            }
            GUI.matrix = previous;
        }
        private void OnDestroy() { if (font != null) Destroy(font); }
    }
}
