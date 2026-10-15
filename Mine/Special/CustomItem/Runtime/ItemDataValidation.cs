using System;
using CelToon.CustomItem.Contracts;

namespace CelToon.CustomItem.Runtime
{
    // Only carrier structure is checked here. Payload semantics belong to the domain adapter.
    public static class ItemDataValidation
    {
        public static bool HasId(string value) => !string.IsNullOrWhiteSpace(value);

        public static string ValidateContainer(ItemContainerDefinition container)
        {
            if (container == null || !HasId(container.ContainerId) || !HasId(container.OwnerId)) return "Container and owner IDs are required.";
            if (!Enum.IsDefined(typeof(ItemContainerKind), container.Kind) || container.Capacity < 0) return "Container kind or capacity is invalid.";
            return null;
        }

        public static string ValidateInstance(ItemInstance item)
        {
            if (item == null || !HasId(item.InstanceId)) return "An instance ID is required.";
            if (item.DefinitionRef == null || !HasId(item.DefinitionRef.PartId)
                || !Enum.IsDefined(typeof(ItemFamily), item.DefinitionRef.Family)) return "The definition reference is invalid.";
            if (!Enum.IsDefined(typeof(ItemRarity), item.Rarity)) return "The rarity is invalid.";
            var content = item.Content;
            if (content == null || !HasId(content.PayloadFormat) || !HasId(content.PayloadJson)
                || !HasId(content.SpecificationId) || content.PayloadVersion < 1 || content.SpecificationVersion < 1
                || content.ContentRevision < 0 || content.GenerationRuleVersion < 0 || content.Affixes == null) return "The content snapshot is invalid.";
            foreach (var affix in content.Affixes)
            {
                if (affix == null || !HasId(affix.AffixId) || !HasId(affix.ParameterPresetId)
                    || !Enum.IsDefined(typeof(ItemAffixRole), affix.Role)
                    || !Enum.IsDefined(typeof(ItemUniqueScope), affix.UniqueScope)
                    || affix.Parameters == null || affix.SemanticGroupIds == null || affix.UniqueGroupIds == null) return "The affix snapshot is invalid.";
                foreach (float parameter in affix.Parameters)
                    if (float.IsNaN(parameter) || float.IsInfinity(parameter)) return "Affix parameters must be finite.";
                foreach (string group in affix.SemanticGroupIds) if (!HasId(group)) return "A semantic group ID is missing.";
                foreach (string group in affix.UniqueGroupIds) if (!HasId(group)) return "A unique group ID is missing.";
            }
            return null;
        }
    }
}
