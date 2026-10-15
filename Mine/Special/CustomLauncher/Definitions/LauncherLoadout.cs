using System;
using CelToon.CustomLauncher.Contracts;

namespace CelToon.CustomLauncher.Definitions
{
    [Serializable]
    public sealed class PartInstance
    {
        public string SlotId;
        public PartDefinition Part;
        public bool OverrideDistribution;
        public int Count = 1;
        public float SpreadDegrees = 20f;
        public PartInstance(string slotId, PartDefinition part) { SlotId = slotId; Part = part; }
    }

    [Serializable]
    public sealed class LauncherLoadout
    {
        public LauncherRecipeDefinition Recipe;
        public PartInstance[] Parts = Array.Empty<PartInstance>();
        public LauncherLoadout(LauncherRecipeDefinition recipe, params PartInstance[] parts)
        { Recipe = recipe; Parts = parts; }
        public LauncherLoadout WithPart(PartKind kind, PartDefinition part)
        {
            var copy = new PartInstance[LauncherSlots.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                var previous = Find((PartKind)i);
                copy[i] = new PartInstance(LauncherSlots.Id((PartKind)i), i == (int)kind ? part : previous?.Part);
                if (previous != null && i != (int)kind)
                { copy[i].OverrideDistribution = previous.OverrideDistribution; copy[i].Count = previous.Count; copy[i].SpreadDegrees = previous.SpreadDegrees; }
            }
            return new LauncherLoadout(Recipe, copy);
        }
        public PartInstance Find(PartKind kind)
        {
            if (Parts != null) foreach (var part in Parts) if (part != null && part.SlotId == LauncherSlots.Id(kind)) return part;
            return null;
        }
        public LauncherLoadout Snapshot()
        {
            if (Parts == null) return new LauncherLoadout(Recipe) { Parts = null };
            var copy = new PartInstance[Parts.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                var part = Parts[i]; if (part == null) continue;
                copy[i] = new PartInstance(part.SlotId, part.Part) { OverrideDistribution = part.OverrideDistribution,
                    Count = part.Count, SpreadDegrees = part.SpreadDegrees };
            }
            return new LauncherLoadout(Recipe, copy);
        }
    }
}
