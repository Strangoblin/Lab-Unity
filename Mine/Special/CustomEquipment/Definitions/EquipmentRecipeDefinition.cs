using System;
using CelToon.CustomEquipment.Contracts;
using UnityEngine;

namespace CelToon.CustomEquipment.Definitions
{
    [CreateAssetMenu(menuName = "CelToon/Custom Equipment/Recipe")]
    public sealed class EquipmentRecipeDefinition : ScriptableObject
    {
        public string RecipeId = "equipment.custom", DisplayName = "自定义 / 空配方";
        [TextArea] public string Description;
        public EquipmentStats BaseStats = EquipmentStats.Standard;
        [HideInInspector] public int ResourceSchemaVersion;
        public EquipmentPartDefinition[] Parts = Array.Empty<EquipmentPartDefinition>();
        public EquipmentLoadout CreateLoadout()
        {
            var instances = new EquipmentPartInstance[EquipmentSlots.Count];
            for (int i = 0; i < instances.Length; i++) instances[i] = new EquipmentPartInstance(EquipmentSlots.Id(EquipmentSlots.At(i)), null);
            if (Parts != null) foreach (var part in Parts)
                if (part != null && EquipmentSlots.Supported(part.Kind)) instances[EquipmentSlots.Index(part.Kind)].Part = part;
            return new EquipmentLoadout(this, instances);
        }
    }
}
