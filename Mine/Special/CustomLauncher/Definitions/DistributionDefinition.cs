using CelToon.CustomLauncher.Contracts;
using UnityEngine;

namespace CelToon.CustomLauncher.Definitions
{
    [CreateAssetMenu(menuName = "CelToon/Custom Launcher/Distribution")]
    public sealed class DistributionDefinition : PartDefinition
    {
        public override PartKind Kind => PartKind.Distribution;
        public DistributionMode Mode = DistributionMode.Single;
        [Range(1, 128)] public int Count = 1;
        [Range(0f, 180f)] public float SpreadDegrees = 20f;
    }
}
