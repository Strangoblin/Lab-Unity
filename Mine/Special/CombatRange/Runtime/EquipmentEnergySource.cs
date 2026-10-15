using CelToon.CustomEquipment.Runtime;
using CelToon.CustomLauncher.Contracts;

namespace CelToon.CombatRange
{
    // Integration owns the dependency on both modules; neither compiler knows the other.
    public sealed class EquipmentEnergySource : ILauncherEnergySource
    {
        private readonly EquipmentHost host;
        public EquipmentEnergySource(EquipmentHost host) { this.host = host; }
        public float Current => host != null && host.State != null ? host.State.Energy : 0;
        public float Capacity => host != null && host.State?.Plan != null ? host.State.Plan.Stats.MaxEnergy : 0;
        public bool TryReserve(float baseCost, out ILauncherEnergyReservation reservation)
        {
            reservation = null;
            if (host == null || host.State == null) return false;
            var state = host.State; float before = state.Energy;
            if (!state.TrySpendEnergy(baseCost)) return false;
            reservation = new Reservation(state, before - state.Energy); return true;
        }
        private sealed class Reservation : ILauncherEnergyReservation
        {
            private EquipmentState state;
            private readonly float amount;
            private bool committed;
            public Reservation(EquipmentState state, float amount) { this.state = state; this.amount = amount; }
            public void Commit() { committed = true; }
            public void Dispose()
            {
                if (state == null) return;
                if (!committed) state.RechargeEnergy(amount);
                state = null;
            }
        }
    }
}
