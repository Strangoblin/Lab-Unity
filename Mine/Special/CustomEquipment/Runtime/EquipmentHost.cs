using System;
using CelToon.CustomEquipment.Compilation;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Definitions;
using CelToon.CustomLauncher.Contracts;
using UnityEngine;

namespace CelToon.CustomEquipment.Runtime
{
    public sealed class EquipmentHost : MonoBehaviour, IDamageReceiver, IStatusReceiver, IImpulseReceiver
    {
        [SerializeField] private EquipmentLoadout loadout;
        public bool AutoTick = true;
        public bool SprintRequested;
        public EquipmentState State { get; private set; }
        public EquipmentCompileResult LastCompilation { get; private set; }
        public EquipmentLoadout Loadout => loadout?.Snapshot();
        public Vector3 ImpulseVelocity { get; private set; }
        public event Action<EquipmentDamageReport> Damaged;
        private void Awake() { if (loadout?.Recipe != null) TryEquip(loadout); }
        public bool TryEquip(EquipmentLoadout draft)
        {
            if (!TryCommitCompiled(EquipmentCompiler.Compile(draft))) return false;
            loadout = draft.Snapshot(); return true;
        }
        /// <summary>Commit compiler-validated values while preserving current resources, statuses and timers.</summary>
        public bool TryCommitCompiled(EquipmentCompileResult result)
        {
            LastCompilation = result;
            if (result == null || !result.CanCommit) return false;
            if (State == null) State = new EquipmentState();
            State.Equip(result.Plan); loadout = null; return true;
        }
        public void ReceiveDamage(DamageInfo info)
        {
            if (State == null) return;
            var report = State.ApplyDamage(info.Amount);
            if (!report.Ignored) Damaged?.Invoke(report);
        }
        public void ReceiveStatus(StatusInfo info) { State?.ApplyStatus(info); }
        public void ReceiveImpulse(Vector3 velocity)
        {
            if (State?.Plan == null || State.IsDead || !LauncherLimits.Finite(velocity)) return;
            ImpulseVelocity = Vector3.ClampMagnitude(ImpulseVelocity + Vector3.ClampMagnitude(velocity, 30) * (1 - State.Plan.Stats.ImpulseResistance), 30);
        }
        public void Tick(float dt, bool sprintRequested = false)
        {
            if (!EquipmentLimits.Range(dt, 0, 10)) return;
            State?.Tick(dt, sprintRequested); ImpulseVelocity *= Mathf.Exp(-5 * dt);
            if (State != null && State.IsDead) ImpulseVelocity = Vector3.zero;
        }
        private void Update() { if (AutoTick) Tick(Time.deltaTime, SprintRequested); }
        public void ResetPlayer() { State?.Reset(); ImpulseVelocity = Vector3.zero; SprintRequested = false; }
    }
}
