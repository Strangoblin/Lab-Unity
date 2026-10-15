using System;
using CelToon.CustomEquipment.Contracts;

namespace CelToon.CustomEquipment.Definitions
{
    [Serializable]
    public sealed class EquipmentPartInstance
    {
        public string SlotId;
        public EquipmentPartDefinition Part;
        public EquipmentPartInstance(string slotId, EquipmentPartDefinition part) { SlotId = slotId; Part = part; }
    }
    [Serializable]
    public sealed class EquipmentLoadout
    {
        public EquipmentRecipeDefinition Recipe;
        public EquipmentPartInstance[] Parts = Array.Empty<EquipmentPartInstance>();
        public EquipmentLoadout(EquipmentRecipeDefinition recipe, params EquipmentPartInstance[] parts) { Recipe = recipe; Parts = parts; }
        public EquipmentPartInstance Find(EquipmentKind kind)
        {
            if (Parts != null) foreach (var instance in Parts) if (instance != null && instance.SlotId == EquipmentSlots.Id(kind)) return instance;
            return null;
        }
        public EquipmentLoadout WithPart(EquipmentKind kind, EquipmentPartDefinition part)
        {
            if (!EquipmentSlots.Supported(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            var copy = new EquipmentPartInstance[EquipmentSlots.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = new EquipmentPartInstance(EquipmentSlots.Id(EquipmentSlots.At(i)), i == EquipmentSlots.Index(kind) ? part : Find(EquipmentSlots.At(i))?.Part);
            return new EquipmentLoadout(Recipe, copy);
        }
        public EquipmentLoadout Snapshot()
        {
            if (Parts == null) return new EquipmentLoadout(Recipe) { Parts = null };
            var copy = new EquipmentPartInstance[Parts.Length];
            for (int i = 0; i < copy.Length; i++) if (Parts[i] != null) copy[i] = new EquipmentPartInstance(Parts[i].SlotId, Parts[i].Part);
            return new EquipmentLoadout(Recipe, copy);
        }
    }
}
