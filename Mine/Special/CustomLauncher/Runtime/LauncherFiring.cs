using System;
using CelToon.CustomLauncher.Contracts;
using UnityEngine;

namespace CelToon.CustomLauncher.Runtime
{
    public static class LauncherFiring
    {
        public static ProjectileCommand[] BuildCommands(CompiledLauncherPlan plan, FireAim aim)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!aim.IsValid) throw new ArgumentException("Invalid fire aim.", nameof(aim));
            var commands = new ProjectileCommand[plan.Count];
            var forward = aim.Forward.normalized;
            var axis = Vector3.Cross(Vector3.Cross(forward, aim.Up.normalized).normalized, forward).normalized;
            var right = Vector3.Cross(axis, forward).normalized;
            for (int i = 0; i < commands.Length; i++)
            {
                int shot = i % plan.DistributionCount;
                float angle = plan.Distribution == DistributionMode.Ring ? 360f * shot / plan.DistributionCount
                    : plan.Distribution == DistributionMode.Fan && plan.DistributionCount > 1
                        ? -plan.SpreadDegrees * .5f + plan.SpreadDegrees * shot / (plan.DistributionCount - 1) : 0f;
                var direction = (Quaternion.AngleAxis(angle, axis) * forward).normalized;
                var origin = aim.Origin;
                if (plan.Distribution == DistributionMode.Cone)
                {
                    float azimuth = shot * 137.50776f * Mathf.Deg2Rad;
                    float radial = Mathf.Sqrt((shot + .5f) / plan.DistributionCount) * Mathf.Tan(Mathf.Min(85f, plan.SpreadDegrees * .5f) * Mathf.Deg2Rad);
                    direction = (forward + (right * Mathf.Cos(azimuth) + axis * Mathf.Sin(azimuth)) * radial).normalized;
                }
                if (plan.Distribution == DistributionMode.Parallel) origin += right * (shot - (plan.DistributionCount - 1) * .5f) * .35f;
                if (plan.Distribution == DistributionMode.Cross)
                {
                    var rotationAxis = shot % 2 == 0 ? axis : right;
                    float crossAngle = shot == 0 ? 0f : (shot % 4 < 2 ? 1f : -1f) * plan.SpreadDegrees * .5f;
                    direction = (Quaternion.AngleAxis(crossAngle, rotationAxis) * forward).normalized;
                }
                commands[i] = new ProjectileCommand(origin, direction, plan.Projectile, aim.OwnerId, aim.TeamId,
                    plan.Program, i / plan.DistributionCount * plan.BurstInterval);
            }
            return commands;
        }

        public static FireResult TryFire(LauncherState state, IProjectileEmitter emitter, FireAim aim, float now)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (emitter == null) throw new ArgumentNullException(nameof(emitter));
            var plan = state.Plan;
            if (plan == null) return FireResult.NoPlan;
            if (!plan.HasProjectile) return FireResult.EmptyPayload;
            if (!LauncherLimits.Finite(now) || !LauncherLimits.Finite(now + plan.Cooldown)) return FireResult.InvalidTime;
            if (!aim.IsValid) return FireResult.InvalidAim;
            if (now < state.NextFireTime) return FireResult.CoolingDown;
            var source = state.EnergySource;
            if (source == null)
            {
                if (state.Energy < plan.EnergyCost) return FireResult.InsufficientEnergy;
                if (!emitter.TryEmit(BuildCommands(plan, aim))) return FireResult.CapacityExceeded;
                state.Commit(now, plan, true);
                return FireResult.Fired;
            }
            // The owner decides affordability after applying its own cost multiplier.
            // Build before reserving so invalid command construction cannot hold energy.
            var commands = BuildCommands(plan, aim);
            if (!source.TryReserve(plan.EnergyCost, out var reservation)) return FireResult.InsufficientEnergy;
            if (reservation == null) throw new InvalidOperationException("Energy source returned an empty reservation.");
            using (reservation)
            {
                if (!emitter.TryEmit(commands)) return FireResult.CapacityExceeded;
                reservation.Commit();
                state.Commit(now, plan, false);
            }
            return FireResult.Fired;
        }
    }
}
