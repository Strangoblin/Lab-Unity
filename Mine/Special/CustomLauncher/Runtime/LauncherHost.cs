using CelToon.CustomLauncher.Compilation;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Definitions;
using UnityEngine;

namespace CelToon.CustomLauncher.Runtime
{
    public sealed class LauncherHost : MonoBehaviour
    {
        [SerializeField] private LauncherLoadout loadout;
        [SerializeField, Min(0f)] private float maxEnergy = 100f;
        public LauncherState State { get; private set; }
        public CompileResult LastCompilation { get; private set; }
        public LauncherLoadout Loadout => loadout?.Snapshot();
        private void Awake() { EnsureState(); if (loadout != null) TryEquip(loadout); }
        private void EnsureState() { if (State == null) State = new LauncherState(maxEnergy); }
        public void BindEnergySource(ILauncherEnergySource source) { EnsureState(); State.BindEnergySource(source); }
        public bool TryEquip(LauncherLoadout draft)
        {
            if (!TryCommitCompiled(LauncherCompiler.Compile(draft))) return false;
            loadout = draft.Snapshot();
            return true;
        }
        /// <summary>Apply a validated value plan without refilling energy or clearing firing state.</summary>
        public bool TryCommitCompiled(CompileResult result)
        {
            LastCompilation = result;
            if (result == null || !result.CanCommit) return false;
            EnsureState(); State.Equip(result.Plan); loadout = null;
            return true;
        }
        public FireResult TryFire(IProjectileEmitter emitter, FireAim aim, float now)
            => LauncherFiring.TryFire(State, emitter, aim, now);
    }
}
