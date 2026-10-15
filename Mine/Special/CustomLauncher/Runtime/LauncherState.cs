using System;
using CelToon.CustomLauncher.Contracts;

namespace CelToon.CustomLauncher.Runtime
{
    public sealed class LauncherState
    {
        public CompiledLauncherPlan Plan { get; private set; }
        private float internalEnergy;
        private readonly float internalMaxEnergy;
        private ILauncherEnergySource energySource;
        public float Energy => energySource != null ? energySource.Current : internalEnergy;
        public float MaxEnergy => energySource != null ? energySource.Capacity : internalMaxEnergy;
        internal ILauncherEnergySource EnergySource => energySource;
        public float NextFireTime { get; private set; } = float.NegativeInfinity;
        public int ShotCount { get; private set; }
        public LauncherState(float maxEnergy = 100f)
        {
            if (!LauncherLimits.InRange(maxEnergy, 0f, 1000000f)) throw new ArgumentOutOfRangeException(nameof(maxEnergy));
            internalMaxEnergy = maxEnergy; internalEnergy = maxEnergy;
        }
        /// <summary>Binding preserves both batteries and firing state; null restores the local battery.</summary>
        public void BindEnergySource(ILauncherEnergySource source) => energySource = source;
        /// <summary>Explicitly clears firing counters and cooldown; preserves the plan and both energy stores.</summary>
        public void ResetFiring() { NextFireTime = float.NegativeInfinity; ShotCount = 0; }
        public void Equip(CompiledLauncherPlan plan)
        { Plan = plan ?? throw new ArgumentNullException(nameof(plan)); }
        public void Recharge(float amount)
        {
            if (!LauncherLimits.InRange(amount, 0f, 1000000f)) throw new ArgumentOutOfRangeException(nameof(amount));
            // Shared energy is recharged by its owner; launcher recharge must never duplicate it.
            if (energySource == null) internalEnergy = Math.Min(internalMaxEnergy, internalEnergy + amount);
        }
        internal void Commit(float now, CompiledLauncherPlan plan, bool spendInternalEnergy)
        {
            if (spendInternalEnergy) internalEnergy -= plan.EnergyCost;
            NextFireTime = now + plan.Cooldown; ShotCount++;
        }
    }
}
