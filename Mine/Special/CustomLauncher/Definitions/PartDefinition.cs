using CelToon.CustomLauncher.Contracts;
using UnityEngine;

namespace CelToon.CustomLauncher.Definitions
{
    public abstract class PartDefinition : ScriptableObject
    {
        public string PartId;
        public string DisplayName;
        [TextArea] public string Description;
        public abstract PartKind Kind { get; }
    }
}
