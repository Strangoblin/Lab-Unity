using System;
using System.Collections.Generic;
using System.Linq;
using CelToon.CustomEquipment.Contracts;

namespace CelToon.CustomEquipment.Definitions
{
    /// <summary>Saved per-item values consumed without creating or mutating a ScriptableObject.</summary>
    public sealed class EquipmentResolvedPart
    {
        public string SlotId { get; }
        public string PartId { get; }
        public string DisplayName { get; }
        public EquipmentKind Kind { get; }
        public bool IsEmpty { get; }
        public IReadOnlyList<EquipmentStep> PrimaryOperations { get; }
        public IReadOnlyList<EquipmentStep> Affixes { get; }

        public EquipmentResolvedPart(string slotId, string partId, string displayName, EquipmentKind kind,
            IEnumerable<EquipmentStep> primaryOperations, IEnumerable<EquipmentStep> affixes)
        {
            SlotId = slotId; PartId = partId; DisplayName = displayName; Kind = kind;
            PrimaryOperations = primaryOperations == null ? null : Array.AsReadOnly(primaryOperations.ToArray());
            Affixes = affixes == null ? null : Array.AsReadOnly(affixes.ToArray());
        }

        private EquipmentResolvedPart(string slotId) { SlotId = slotId; IsEmpty = true; }
        public static EquipmentResolvedPart Empty(string slotId) => new EquipmentResolvedPart(slotId);
        public static EquipmentResolvedPart FromDefinition(string slotId, EquipmentPartDefinition part)
            => part == null ? Empty(slotId) : new EquipmentResolvedPart(slotId, part.PartId, part.DisplayName,
                part.Kind, part.PrimaryOperations, part.Affixes);
        public static EquipmentResolvedPart FromInstance(EquipmentPartInstance instance)
            => instance == null ? Empty(null) : FromDefinition(instance.SlotId, instance.Part);
    }
}
