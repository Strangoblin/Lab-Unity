using System;
using System.Collections.Generic;
using UnityEngine;

namespace CelToon.CustomLauncher.Contracts
{
    public enum PartKind { Projectile, Distribution, Trajectory, Damage, Impact, Trigger, Modifier, FireControl }
    public enum DistributionMode { Single, Fan, Ring, Cone, Parallel, Cross }
    public enum LauncherStage { Projectile, Modifier, Distribution, Trajectory, Damage, Impact, Trigger, FireControl }
    public enum FireResult { Fired, EmptyPayload, NoPlan, InvalidAim, InvalidTime, CoolingDown, InsufficientEnergy, CapacityExceeded }
    public enum EffectOperation
    {
        Gravity, Homing, Wave, Helix, Acceleration, Drag, Return,
        Burn, Poison, Slow, Stun, Mark, BonusDamage,
        Explosion, Bounce, Pierce, Chain, Push, Pull,
        TimedSplit, HitSplit, ExpirySplit,
        DamageScale, SpeedScale, RadiusScale, LifetimeScale, CostScale,
        CooldownScale, Burst
    }
    public enum StatusKind { Burn, Poison, Slow, Stun, Mark }
    public static class LauncherSlots
    {
        public const int Count = 8;
        public static string Id(PartKind kind) => kind.ToString().ToLowerInvariant() + ".main";
        public static bool Supported(PartKind kind) => (int)kind >= 0 && (int)kind < Count;
    }
    public static class LauncherLimits
    {
        public const int MaxProjectilesPerShot = 128, MaxGeneratedPerShot = 4096;
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        public static bool InRange(float value, float min, float max) => Finite(value) && value >= min && value <= max;
    }
    [Serializable]
    public struct EffectStep
    {
        public EffectOperation Operation;
        public float A, B, Delay;
        public int Count;
        public EffectStep(EffectOperation operation, float a = 1f, float b = 1f, int count = 1, float delay = 0f)
        { Operation = operation; A = a; B = b; Count = count; Delay = delay; }
        public PartKind Kind
        {
            get
            {
                int value = (int)Operation;
                if (value <= (int)EffectOperation.Return) return PartKind.Trajectory;
                if (value <= (int)EffectOperation.BonusDamage) return PartKind.Damage;
                if (value <= (int)EffectOperation.Pull) return PartKind.Impact;
                if (value <= (int)EffectOperation.ExpirySplit) return PartKind.Trigger;
                if (value <= (int)EffectOperation.CostScale) return PartKind.Modifier;
                return PartKind.FireControl;
            }
        }
        public bool IsValid
        {
            get
            {
                if (!Enum.IsDefined(typeof(EffectOperation), Operation) || !LauncherLimits.InRange(A, 0f, 1000f)
                    || !LauncherLimits.InRange(B, 0f, 1000f) || !LauncherLimits.InRange(Delay, 0f, 120f) || Count < 1 || Count > 16) return false;
                switch (Operation)
                {
                    case EffectOperation.DamageScale: case EffectOperation.SpeedScale: case EffectOperation.RadiusScale:
                    case EffectOperation.LifetimeScale: case EffectOperation.CostScale: case EffectOperation.CooldownScale:
                        return A >= .05f && A <= 10f;
                    case EffectOperation.Burst: return Count <= 8 && Delay >= .02f && Delay <= 2f;
                    case EffectOperation.TimedSplit: return Delay >= .02f && A <= 180f && B >= .05f && B <= 2f;
                    case EffectOperation.HitSplit: case EffectOperation.ExpirySplit: return A <= 180f && B >= .05f && B <= 2f;
                    case EffectOperation.Explosion: return A >= .1f && A <= 20f && B >= .05f && B <= 10f;
                    case EffectOperation.Chain: return A >= .1f && A <= 30f && B >= .05f && B <= 3f;
                    case EffectOperation.Homing: return A <= 20f && B > 0f && B <= 100f;
                    case EffectOperation.Wave: case EffectOperation.Helix: return A <= 5f && B <= 40f;
                    case EffectOperation.Slow: return A >= .05f && A <= 1f && B <= 30f;
                    case EffectOperation.Mark: return A >= 1f && A <= 5f && B <= 30f;
                    case EffectOperation.Burn: case EffectOperation.Poison: case EffectOperation.Stun: return B > 0f && B <= 30f;
                    default: return true;
                }
            }
        }
    }
    public sealed class ProjectileProgram
    {
        public static readonly ProjectileProgram Empty = new ProjectileProgram(Array.Empty<EffectStep>(), 128);
        public IReadOnlyList<EffectStep> Steps { get; }
        public int MaxGenerated { get; }
        public ProjectileProgram(EffectStep[] steps, int maxGenerated)
        {
            if (steps == null || steps.Length > 32 || maxGenerated < 1 || maxGenerated > LauncherLimits.MaxGeneratedPerShot)
                throw new ArgumentException("Invalid projectile program.");
            foreach (var step in steps) if (!step.IsValid) throw new ArgumentException("Invalid effect step.");
            Steps = Array.AsReadOnly((EffectStep[])steps.Clone()); MaxGenerated = maxGenerated;
        }
    }
    public readonly struct ProjectileSpec
    {
        public readonly float Damage, Speed, Radius, Lifetime;
        public readonly Color Color;
        public ProjectileSpec(float damage, float speed, float radius, float lifetime, Color color)
        { Damage = damage; Speed = speed; Radius = radius; Lifetime = lifetime; Color = color; }
        public bool IsValid => LauncherLimits.InRange(Damage, 0f, 1000000f)
            && LauncherLimits.InRange(Speed, .1f, 1000f) && LauncherLimits.InRange(Radius, .01f, 5f)
            && LauncherLimits.InRange(Lifetime, .02f, 120f)
            && LauncherLimits.InRange(Color.r, 0f, 1f) && LauncherLimits.InRange(Color.g, 0f, 1f)
            && LauncherLimits.InRange(Color.b, 0f, 1f) && LauncherLimits.InRange(Color.a, 0f, 1f);
    }
    public sealed class CompiledLauncherPlan
    {
        private static readonly IReadOnlyList<LauncherStage> stages = Array.AsReadOnly(new[] {
            LauncherStage.Projectile, LauncherStage.Modifier, LauncherStage.Distribution, LauncherStage.Trajectory,
            LauncherStage.Damage, LauncherStage.Impact, LauncherStage.Trigger, LauncherStage.FireControl });
        public string RecipeId { get; }
        public string ProjectileId { get; }
        public ProjectileSpec Projectile { get; }
        public ProjectileProgram Program { get; }
        public DistributionMode Distribution { get; }
        public int DistributionCount { get; }
        public int BurstCount { get; }
        public int Count => HasProjectile ? DistributionCount * BurstCount : 0;
        public bool HasProjectile => !string.IsNullOrEmpty(ProjectileId);
        public float SpreadDegrees { get; }
        public float Cooldown { get; }
        public float EnergyCost { get; }
        public float BurstInterval { get; }
        public IReadOnlyList<LauncherStage> Stages => stages;
        public CompiledLauncherPlan(string recipeId, string projectileId, ProjectileSpec projectile,
            DistributionMode distribution, int count, float spreadDegrees, float cooldown, float energyCost,
            ProjectileProgram program = null, int burstCount = 1, float burstInterval = .1f)
        {
            if (string.IsNullOrWhiteSpace(recipeId) || (!string.IsNullOrEmpty(projectileId) && !projectile.IsValid)
                || !Enum.IsDefined(typeof(DistributionMode), distribution) || count < 1 || count > 128 || burstCount < 1 || burstCount > 8
                || count * burstCount > 128 || (distribution == DistributionMode.Single && count != 1)
                || !LauncherLimits.InRange(spreadDegrees, 0f, 180f) || !LauncherLimits.InRange(cooldown, .02f, 60f)
                || !LauncherLimits.InRange(energyCost, 0f, 1000000f) || !LauncherLimits.InRange(burstInterval, .02f, 2f))
                throw new ArgumentException("Invalid compiled launcher plan.");
            RecipeId = recipeId; ProjectileId = projectileId ?? ""; Projectile = projectile;
            Distribution = distribution; DistributionCount = count; SpreadDegrees = spreadDegrees;
            Cooldown = cooldown; EnergyCost = energyCost; Program = program ?? ProjectileProgram.Empty;
            BurstCount = burstCount; BurstInterval = burstInterval;
        }
    }
    public readonly struct FireAim
    {
        public readonly Vector3 Origin, Forward, Up;
        public readonly int OwnerId, TeamId;
        public FireAim(Vector3 origin, Vector3 forward, Vector3 up, int ownerId = 0, int teamId = 0)
        { Origin = origin; Forward = forward; Up = up; OwnerId = ownerId; TeamId = teamId; }
        public bool IsValid => LauncherLimits.Finite(Origin) && LauncherLimits.Finite(Forward)
            && LauncherLimits.Finite(Up) && LauncherLimits.InRange(Origin.magnitude, 0f, 1000000f)
            && LauncherLimits.InRange(Forward.magnitude, .0001f, 10000f) && LauncherLimits.InRange(Up.magnitude, .0001f, 10000f)
            && Vector3.Cross(Forward.normalized, Up.normalized).sqrMagnitude > .000001f;
    }
    public readonly struct ProjectileCommand
    {
        public readonly Vector3 Origin, Direction;
        public readonly ProjectileSpec Spec;
        public readonly int OwnerId, TeamId;
        public readonly ProjectileProgram Program;
        public readonly float Delay;
        public ProjectileCommand(Vector3 origin, Vector3 direction, ProjectileSpec spec, int ownerId, int teamId,
            ProjectileProgram program = null, float delay = 0f)
        { Origin = origin; Direction = direction; Spec = spec; OwnerId = ownerId; TeamId = teamId; Program = program ?? ProjectileProgram.Empty; Delay = delay; }
    }
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly Vector3 Point, Direction;
        public readonly int OwnerId, TeamId;
        public DamageInfo(float amount, Vector3 point, Vector3 direction, int ownerId, int teamId)
        { Amount = amount; Point = point; Direction = direction; OwnerId = ownerId; TeamId = teamId; }
    }
    public readonly struct StatusInfo
    {
        public readonly StatusKind Kind;
        public readonly float Strength, Duration;
        public readonly int OwnerId, TeamId;
        public StatusInfo(StatusKind kind, float strength, float duration, int ownerId, int teamId)
        { Kind = kind; Strength = strength; Duration = duration; OwnerId = ownerId; TeamId = teamId; }
    }
    public interface IDamageReceiver { void ReceiveDamage(DamageInfo damage); }
    public interface IStatusReceiver { void ReceiveStatus(StatusInfo status); }
    public interface IImpulseReceiver { void ReceiveImpulse(Vector3 velocity); }
    public interface IProjectileEmitter { bool TryEmit(IReadOnlyList<ProjectileCommand> commands); }
    /// <summary>
    /// Supplies shared energy without coupling the launcher to its owner. The source applies any
    /// owner-specific cost multiplier and reserves the full cost atomically. A failed reservation
    /// leaves its energy unchanged. A successful reservation must be non-null.
    /// </summary>
    public interface ILauncherEnergySource
    {
        float Current { get; }
        float Capacity { get; }
        bool TryReserve(float baseCost, out ILauncherEnergyReservation reservation);
    }
    /// <summary>
    /// Commit accepts a completed emission and must not throw. Dispose rolls back an uncommitted
    /// reservation, leaves a committed one unchanged, and must be safe to call repeatedly.
    /// </summary>
    public interface ILauncherEnergyReservation : IDisposable
    {
        void Commit();
    }
    public sealed class CompileDiagnostic
    {
        public string Code { get; }
        public string SlotId { get; }
        public string PartId { get; }
        public string Message { get; }
        public CompileDiagnostic(string code, string slotId, string partId, string message)
        { Code = code; SlotId = slotId ?? ""; PartId = partId ?? ""; Message = message; }
        public override string ToString() => Code + " [" + SlotId + "] " + Message;
    }
}
