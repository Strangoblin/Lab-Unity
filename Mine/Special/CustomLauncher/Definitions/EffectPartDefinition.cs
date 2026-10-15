using System;
using CelToon.CustomLauncher.Contracts;
using UnityEngine;

namespace CelToon.CustomLauncher.Definitions
{
    [CreateAssetMenu(menuName = "CelToon/Custom Launcher/Effect Part")]
    public sealed class EffectPartDefinition : PartDefinition
    {
        public PartKind Category = PartKind.Impact;
        public override PartKind Kind => Category;
        [Min(0f)] public float EnergyPerProjectile;
        public EffectStep[] Operations = Array.Empty<EffectStep>();
    }
}
