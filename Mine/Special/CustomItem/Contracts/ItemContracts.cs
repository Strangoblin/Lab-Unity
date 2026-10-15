using System;

namespace CelToon.CustomItem.Contracts
{
    public enum ItemFamily { LauncherPart = 0, EquipmentCore = 1 }
    public enum ItemRarity { Common = 0, Uncommon = 1, Rare = 2, Epic = 3, Legendary = 4 }
    public enum ItemAffixRole { Baseline = 0, Ordinary = 1, Signature = 2 }
    public enum ItemUniqueScope { EquipmentLoadout = 0, LauncherLoadout = 1, PlayerBuild = 2 }
    public enum ItemContainerKind { Backpack = 0, Warehouse = 1, Equipment = 2, Launcher = 3, World = 4, Reward = 5 }

    [Serializable]
    public sealed class ItemDefinitionRef
    {
        public ItemFamily Family;
        public string PartId;
        public ItemDefinitionRef Clone() => new ItemDefinitionRef { Family = Family, PartId = PartId };
    }

    [Serializable]
    public sealed class ItemAffixSnapshot
    {
        public string AffixId;
        public string ParameterPresetId;
        public ItemAffixRole Role;
        public float[] Parameters = Array.Empty<float>();
        public string[] SemanticGroupIds = Array.Empty<string>();
        public string[] UniqueGroupIds = Array.Empty<string>();
        public ItemUniqueScope UniqueScope;
        public bool HighImpact;
        public ItemAffixSnapshot Clone() => new ItemAffixSnapshot {
            AffixId = AffixId, ParameterPresetId = ParameterPresetId, Role = Role,
            Parameters = Parameters == null ? null : (float[])Parameters.Clone(),
            SemanticGroupIds = SemanticGroupIds == null ? null : (string[])SemanticGroupIds.Clone(),
            UniqueGroupIds = UniqueGroupIds == null ? null : (string[])UniqueGroupIds.Clone(),
            UniqueScope = UniqueScope, HighImpact = HighImpact
        };
    }

    [Serializable]
    public sealed class ItemContentSnapshot
    {
        public string PayloadFormat;
        public int PayloadVersion = 1;
        public string PayloadJson;
        public string SpecificationId;
        public int SpecificationVersion = 1;
        public int ContentRevision = 1;
        public int GenerationRuleVersion = 1;
        public ItemAffixSnapshot[] Affixes = Array.Empty<ItemAffixSnapshot>();
        public ItemContentSnapshot Clone()
        {
            ItemAffixSnapshot[] affixes = Affixes == null ? null : new ItemAffixSnapshot[Affixes.Length];
            if (affixes != null) for (int i = 0; i < affixes.Length; i++) affixes[i] = Affixes[i]?.Clone();
            return new ItemContentSnapshot { PayloadFormat = PayloadFormat, PayloadVersion = PayloadVersion,
                PayloadJson = PayloadJson, SpecificationId = SpecificationId, SpecificationVersion = SpecificationVersion,
                ContentRevision = ContentRevision, GenerationRuleVersion = GenerationRuleVersion, Affixes = affixes };
        }
    }

    [Serializable]
    public sealed class ItemInstance
    {
        public string InstanceId;
        public ItemDefinitionRef DefinitionRef;
        public ItemRarity Rarity;
        public string DisplayName;
        public string Description;
        public ItemContentSnapshot Content;
        public ItemInstance Clone() => new ItemInstance { InstanceId = InstanceId,
            DefinitionRef = DefinitionRef?.Clone(), Rarity = Rarity, DisplayName = DisplayName,
            Description = Description, Content = Content?.Clone() };
    }

    [Serializable]
    public sealed class ItemLocation
    {
        public string ContainerId;
        public string PositionKey;
        public ItemLocation Clone() => new ItemLocation { ContainerId = ContainerId, PositionKey = PositionKey };
    }

    [Serializable]
    public sealed class ItemContainerDefinition
    {
        public string ContainerId;
        public string OwnerId;
        public ItemContainerKind Kind;
        public int Capacity;
        public ItemContainerDefinition Clone() => new ItemContainerDefinition {
            ContainerId = ContainerId, OwnerId = OwnerId, Kind = Kind, Capacity = Capacity };
    }

    [Serializable]
    public sealed class ItemOperationResult
    {
        public bool Success;
        public string Code;
        public string Message;
        public string[] InstanceIds = Array.Empty<string>();
        public ItemOperationResult Clone() => new ItemOperationResult { Success = Success, Code = Code,
            Message = Message, InstanceIds = InstanceIds == null ? null : (string[])InstanceIds.Clone() };
    }

    [Serializable]
    public sealed class ItemLocationRecord
    {
        public string InstanceId;
        public ItemLocation Location;
    }

    [Serializable]
    public sealed class ItemRequestReceipt
    {
        public string RequestId;
        public string Operation;
        public string ArgumentKey;
        public ItemOperationResult Result;
    }

    [Serializable]
    public sealed class ItemStoreSnapshot
    {
        public int SchemaVersion = 1;
        public ItemContainerDefinition[] Containers = Array.Empty<ItemContainerDefinition>();
        public ItemInstance[] Items = Array.Empty<ItemInstance>();
        public ItemLocationRecord[] Locations = Array.Empty<ItemLocationRecord>();
        public ItemRequestReceipt[] Receipts = Array.Empty<ItemRequestReceipt>();
    }
}
