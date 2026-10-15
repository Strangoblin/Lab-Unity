using System;
using UnityEngine;

namespace CelToon.CustomLauncher.Definitions
{
    [CreateAssetMenu(menuName = "CelToon/Custom Launcher/Catalog")]
    public sealed class PartCatalog : ScriptableObject
    {
        public PartDefinition[] Parts = Array.Empty<PartDefinition>();
        public LauncherRecipeDefinition[] Recipes = Array.Empty<LauncherRecipeDefinition>();
    }
}
