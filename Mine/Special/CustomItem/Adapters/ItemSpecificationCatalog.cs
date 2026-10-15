using System;
using CelToon.CustomItem.Contracts;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomLauncher.Contracts;
using UnityEngine;

namespace CelToon.CustomItem.Adapters
{
    [Serializable]
    public sealed class LauncherItemPayload
    {
        public PartKind Kind;
        public float Damage, Speed, Radius, Lifetime, EnergyPerProjectile;
        public Color Color = Color.white;
        public DistributionMode Distribution = DistributionMode.Single;
        public int Count = 1;
        public float SpreadDegrees;
        public EffectStep[] Operations = Array.Empty<EffectStep>();
    }

    [Serializable]
    public sealed class EquipmentItemPayload
    {
        public EquipmentKind Kind;
        public EquipmentStep[] PrimaryOperations = Array.Empty<EquipmentStep>();
        public EquipmentStep[] Affixes = Array.Empty<EquipmentStep>();
    }

    [CreateAssetMenu(menuName = "CelToon/Custom Item/Specification")]
    public sealed class ItemSpecificationDefinition : ScriptableObject
    {
        public string SpecificationId, PartId, DisplayName;
        [TextArea] public string Description;
        public int SpecificationVersion = 1, ContentRevision = 1, GenerationRuleVersion = 1;
        public ItemFamily Family;
        public ItemRarity Rarity;
        public bool Production = true;
        public ItemAffixSnapshot[] Affixes = Array.Empty<ItemAffixSnapshot>();
        public LauncherItemPayload Launcher = new LauncherItemPayload();
        public EquipmentItemPayload Equipment = new EquipmentItemPayload();
    }

    [Serializable]
    public sealed class ItemWorldSample
    {
        public string SpawnId, SpecificationId;
        public Vector3 Position;
    }

    [CreateAssetMenu(menuName = "CelToon/Custom Item/Specification Catalog")]
    public sealed class ItemSpecificationCatalog : ScriptableObject
    {
        public ItemSpecificationDefinition[] Specifications = Array.Empty<ItemSpecificationDefinition>();
        public string StarterSpecificationId;
        public ItemWorldSample[] WorldSamples = Array.Empty<ItemWorldSample>();
    }
}
