using System;
using System.Collections.Generic;
using System.Linq;
using CelToon.CustomLauncher.Contracts;

namespace CelToon.CustomLauncher.Definitions
{
    /// <summary>A detached value snapshot. It never reads a shared definition after construction.</summary>
    public sealed class LauncherResolvedPart
    {
        public string SlotId { get; }
        public string PartId { get; }
        public string DisplayName { get; }
        public PartKind Kind { get; }
        public bool IsEmpty { get; }
        public ProjectileSpec Projectile { get; }
        public float EnergyPerProjectile { get; }
        public DistributionMode DistributionMode { get; }
        public int Count { get; }
        public float SpreadDegrees { get; }
        public IReadOnlyList<EffectStep> Operations { get; }

        public LauncherResolvedPart(string slotId, string partId, string displayName, PartKind kind,
            ProjectileSpec projectile = default, float energyPerProjectile = 0,
            DistributionMode distributionMode = DistributionMode.Single, int count = 1,
            float spreadDegrees = 0, IEnumerable<EffectStep> operations = null)
        {
            SlotId = slotId; PartId = partId; DisplayName = displayName; Kind = kind;
            Projectile = projectile; EnergyPerProjectile = energyPerProjectile;
            DistributionMode = distributionMode; Count = count; SpreadDegrees = spreadDegrees;
            Operations = operations == null ? null : Array.AsReadOnly(operations.ToArray());
        }

        private LauncherResolvedPart(string slotId) { SlotId = slotId; IsEmpty = true; }
        public static LauncherResolvedPart Empty(string slotId) => new LauncherResolvedPart(slotId);

        public static LauncherResolvedPart FromDefinition(string slotId, PartDefinition part)
        {
            if (part == null) return Empty(slotId);
            if (part is ProjectileDefinition projectile)
                return new LauncherResolvedPart(slotId, part.PartId, part.DisplayName, part.Kind,
                    projectile.ToSpec(), projectile.EnergyPerProjectile);
            if (part is DistributionDefinition distribution)
                return new LauncherResolvedPart(slotId, part.PartId, part.DisplayName, part.Kind,
                    distributionMode: distribution.Mode, count: distribution.Count, spreadDegrees: distribution.SpreadDegrees);
            var effect = part as EffectPartDefinition;
            return new LauncherResolvedPart(slotId, part.PartId, part.DisplayName, part.Kind,
                energyPerProjectile: effect != null ? effect.EnergyPerProjectile : 0, operations: effect?.Operations);
        }

        public static LauncherResolvedPart FromInstance(PartInstance instance)
        {
            if (instance == null) return Empty(null);
            var value = FromDefinition(instance.SlotId, instance.Part);
            if (!instance.OverrideDistribution || !(instance.Part is DistributionDefinition)) return value;
            return new LauncherResolvedPart(value.SlotId, value.PartId, value.DisplayName, value.Kind,
                distributionMode: value.DistributionMode, count: instance.Count, spreadDegrees: instance.SpreadDegrees);
        }
    }
}
