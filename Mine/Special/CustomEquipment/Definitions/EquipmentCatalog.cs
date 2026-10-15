using System;
using UnityEngine;

namespace CelToon.CustomEquipment.Definitions
{
    [CreateAssetMenu(menuName = "CelToon/Custom Equipment/Catalog")]
    public sealed class EquipmentCatalog : ScriptableObject
    {
        public EquipmentPartDefinition[] Parts = Array.Empty<EquipmentPartDefinition>();
        public EquipmentRecipeDefinition[] Recipes = Array.Empty<EquipmentRecipeDefinition>();
    }
}
