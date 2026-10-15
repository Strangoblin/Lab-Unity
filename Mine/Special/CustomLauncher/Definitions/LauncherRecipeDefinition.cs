using System;
using CelToon.CustomLauncher.Contracts;
using UnityEngine;

namespace CelToon.CustomLauncher.Definitions
{
    [CreateAssetMenu(menuName = "CelToon/Custom Launcher/Recipe")]
    public sealed class LauncherRecipeDefinition : ScriptableObject
    {
        public string RecipeId = "recipe.custom";
        public string DisplayName = "Custom";
        [TextArea] public string Description;
        [Min(.02f)] public float Cooldown = .25f;
        [Min(0f)] public float BaseEnergyCost = 1f;
        [Range(1, 128)] public int MaxProjectilesPerShot = 128;
        [Range(1, 4096)] public int MaxGeneratedPerShot = 1024;
        public PartDefinition[] Parts = Array.Empty<PartDefinition>();
        public LauncherLoadout CreateLoadout()
        {
            var instances = new PartInstance[LauncherSlots.Count];
            for (int i = 0; i < instances.Length; i++) instances[i] = new PartInstance(LauncherSlots.Id((PartKind)i), null);
            if (Parts != null) foreach (var part in Parts)
                if (part != null && LauncherSlots.Supported(part.Kind)) instances[(int)part.Kind].Part = part;
            return new LauncherLoadout(this, instances);
        }
    }
}
