using CelToon.CustomLauncher.Contracts;
using UnityEngine;

namespace CelToon.CustomLauncher.Definitions
{
    [CreateAssetMenu(menuName = "CelToon/Custom Launcher/Projectile")]
    public sealed class ProjectileDefinition : PartDefinition
    {
        public override PartKind Kind => PartKind.Projectile;
        [Min(0f)] public float Damage = 10f;
        [Min(.1f)] public float Speed = 30f;
        [Min(.01f)] public float Radius = .08f;
        [Min(.02f)] public float Lifetime = 3f;
        [Min(0f)] public float EnergyPerProjectile = 1f;
        public Color Color = new Color(.2f, .85f, 1f);
        public ProjectileSpec ToSpec() => new ProjectileSpec(Damage, Speed, Radius, Lifetime, Color);
    }
}
