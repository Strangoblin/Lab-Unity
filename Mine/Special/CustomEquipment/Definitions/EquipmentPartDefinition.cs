using System;
using CelToon.CustomEquipment.Contracts;
using UnityEngine;

namespace CelToon.CustomEquipment.Definitions
{
    [CreateAssetMenu(menuName = "CelToon/Custom Equipment/Part")]
    public sealed class EquipmentPartDefinition : ScriptableObject
    {
        public string PartId, DisplayName;
        [TextArea] public string Description;
        public EquipmentKind Kind;
        [Tooltip("仅允许该槽位对应的资源上限；新配件至少一项主属性。")]
        public EquipmentStep[] PrimaryOperations = Array.Empty<EquipmentStep>();
        [Tooltip("固定副词条：回复、减免、移动、应急等；不能增加其他资源上限。")]
        public EquipmentStep[] Affixes = Array.Empty<EquipmentStep>();
        // Serialized v1/v2 data retained for non-destructive migration.
        [HideInInspector] public EquipmentStep[] Operations = Array.Empty<EquipmentStep>();
    }
}
