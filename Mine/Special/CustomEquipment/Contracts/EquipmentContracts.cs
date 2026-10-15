using System;
using System.Collections.Generic;
using UnityEngine;

namespace CelToon.CustomEquipment.Contracts
{
    // Values 0..11 retain both previous schemas; resource cores have new stable IDs.
    public enum EquipmentKind {
        Vitality = 0, Armor = 1, Shield = 2, Resistance = 3, Recovery = 4, Mobility = 5, Reaction = 6, Utility = 7,
        DefenseCore = 8, ShieldGenerator = 9, MobilityDrive = 10, EmergencyDevice = 11,
        HealthCore = 16, ShieldCore = 17, StaminaCore = 18, EnergyCore = 19
    }
    public enum EquipmentOperation
    {
        HealthAdd, HealthScale,
        ArmorAdd, DamageScale,
        ShieldAdd, ShieldRegen, ShieldDelay,
        BurnResistance, PoisonResistance, SlowResistance, StunResistance, MarkResistance,
        HealthRegen, RecoveryDelay,
        MoveScale, SprintScale, ImpulseResistance,
        HitInvulnerability, EmergencyHeal,
        StaminaAdd, StaminaRegen, SprintCostScale, CleanseInterval,
        EnergyAdd, EnergyRegen, EnergyCostScale
    }
    public enum DamageChannel { Direct, Burn, Poison }
    public static class EquipmentSlots
    {
        // A legacy 64-effect group may need one neutral resource primary during migration.
        public const int Count = 4, MaxOperationsPerPart = 65, MaxOperationsPerLoadout = 68;
        public static bool Supported(EquipmentKind kind) => (int)kind >= 16 && (int)kind < 20;
        public static bool Legacy(EquipmentKind kind) => (int)kind >= 0 && (int)kind < 12;
        public static bool EightSlot(EquipmentKind kind) => (int)kind >= 0 && (int)kind < 8;
        public static EquipmentKind At(int index) => index >= 0 && index < Count ? (EquipmentKind)(index + 16) : (EquipmentKind)(-1);
        public static int Index(EquipmentKind kind) => Supported(kind) ? (int)kind - 16 : -1;
        public static string Id(EquipmentKind kind) => kind.ToString().ToLowerInvariant() + ".main";
        public static string Name(EquipmentKind kind)
        {
            var names = new[] { "生命核心", "护盾核心", "体力核心", "能量核心" };
            return Supported(kind) ? names[Index(kind)] : "旧槽位 / 未知";
        }
        public static string Summary(EquipmentKind kind) => kind == EquipmentKind.HealthCore ? "主属性：生命上限"
            : kind == EquipmentKind.ShieldCore ? "主属性：护盾上限"
            : kind == EquipmentKind.StaminaCore ? "主属性：体力上限"
            : kind == EquipmentKind.EnergyCore ? "主属性：能量上限" : "";
        public static bool IsPrimary(EquipmentOperation operation) => operation == EquipmentOperation.HealthAdd || operation == EquipmentOperation.HealthScale
            || operation == EquipmentOperation.ShieldAdd || operation == EquipmentOperation.StaminaAdd || operation == EquipmentOperation.EnergyAdd;
        public static EquipmentOperation PrimaryOperation(EquipmentKind kind) => kind == EquipmentKind.HealthCore ? EquipmentOperation.HealthAdd
            : kind == EquipmentKind.ShieldCore ? EquipmentOperation.ShieldAdd : kind == EquipmentKind.StaminaCore ? EquipmentOperation.StaminaAdd : EquipmentOperation.EnergyAdd;
        public static bool AcceptsPrimary(EquipmentKind kind, EquipmentStep step) => Supported(kind) && step.IsValid && IsPrimary(step.Operation) && step.CoreKind == kind;
        public static bool AcceptsAffix(EquipmentStep step) => step.IsValid && !IsPrimary(step.Operation);
        public static bool Accepts(EquipmentKind kind, EquipmentStep step) => Supported(kind) && step.IsValid
            && (IsPrimary(step.Operation) ? step.CoreKind == kind : true);
    }
    public static class EquipmentLimits
    {
        public static bool Range(float value, float min, float max) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
    }
    [Serializable]
    public struct EquipmentStep
    {
        public EquipmentOperation Operation;
        public float A, B;
        public EquipmentStep(EquipmentOperation operation, float a, float b = 0f) { Operation = operation; A = a; B = b; }
        public EquipmentKind CoreKind {
            get {
                int op = (int)Operation;
                if (!Enum.IsDefined(typeof(EquipmentOperation), Operation)) return (EquipmentKind)(-1);
                if (op <= 3 || (op >= 7 && op <= 13) || op == 17 || op == 18 || op == 22) return EquipmentKind.HealthCore;
                if (op <= 6) return EquipmentKind.ShieldCore;
                if (op >= 23) return EquipmentKind.EnergyCore;
                return EquipmentKind.StaminaCore;
            }
        }
        // Legacy effect family, distinct from the four equip positions.
        public EquipmentKind Kind
        {
            get
            {
                int op = (int)Operation;
                if (op > 22) return (EquipmentKind)(-1);
                if (op <= 1) return EquipmentKind.Vitality;
                if (op <= 3) return EquipmentKind.Armor;
                if (op <= 6) return EquipmentKind.Shield;
                if (op <= 11) return EquipmentKind.Resistance;
                if (op <= 13) return EquipmentKind.Recovery;
                if (op <= 16) return EquipmentKind.Mobility;
                if (op <= 18) return EquipmentKind.Reaction;
                return EquipmentKind.Utility;
            }
        }
        public bool IsValid
        {
            get
            {
                if (!Enum.IsDefined(typeof(EquipmentOperation), Operation) || !EquipmentLimits.Range(B, 0, 120)) return false;
                switch (Operation)
                {
                    case EquipmentOperation.HealthScale: case EquipmentOperation.MoveScale: case EquipmentOperation.SprintScale:
                    case EquipmentOperation.SprintCostScale: case EquipmentOperation.EnergyCostScale: return EquipmentLimits.Range(A, .1f, 4);
                    case EquipmentOperation.DamageScale: return EquipmentLimits.Range(A, .2f, 1);
                    case EquipmentOperation.BurnResistance: case EquipmentOperation.PoisonResistance: case EquipmentOperation.SlowResistance:
                    case EquipmentOperation.StunResistance: case EquipmentOperation.MarkResistance: case EquipmentOperation.ImpulseResistance:
                        return EquipmentLimits.Range(A, 0, .9f);
                    case EquipmentOperation.HitInvulnerability: return EquipmentLimits.Range(A, 0, 2);
                    case EquipmentOperation.EmergencyHeal: return EquipmentLimits.Range(A, .01f, .5f) && EquipmentLimits.Range(B, 1, 120);
                    case EquipmentOperation.CleanseInterval: return EquipmentLimits.Range(A, 1, 120);
                    case EquipmentOperation.ShieldDelay: case EquipmentOperation.RecoveryDelay: return EquipmentLimits.Range(A, 0, 30);
                    default: return EquipmentLimits.Range(A, 0, 1000);
                }
            }
        }
    }
    // A value type: callers receive copies, so changing definitions or a returned Stats cannot mutate a compiled plan.
    [Serializable]
    public struct EquipmentStats
    {
        public float MaxHealth, Armor, DamageScale, MaxShield, ShieldRegen, ShieldDelay;
        public float BurnResistance, PoisonResistance, SlowResistance, StunResistance, MarkResistance;
        public float HealthRegen, RecoveryDelay, MoveSpeed, SprintMultiplier, ImpulseResistance;
        public float HitInvulnerability, EmergencyHealFraction, EmergencyCooldown;
        public float MaxStamina, StaminaRegen, SprintCost, CleanseInterval;
        public float MaxEnergy, EnergyRegen, EnergyCostScale;
        public static EquipmentStats Standard => new EquipmentStats {
            MaxHealth = 100, DamageScale = 1, ShieldDelay = 3, RecoveryDelay = 4,
            MoveSpeed = 3, SprintMultiplier = 1.7f, MaxStamina = 100, StaminaRegen = 15, SprintCost = 22,
            MaxEnergy = 100, EnergyRegen = 10, EnergyCostScale = 1
        };
        public bool IsValid
        {
            get
            {
                foreach (float n in new[] { Armor, MaxShield, ShieldRegen, HealthRegen, MaxStamina, StaminaRegen, SprintCost, MaxEnergy, EnergyRegen })
                    if (!EquipmentLimits.Range(n, 0, 10000)) return false;
                foreach (float n in new[] { BurnResistance, PoisonResistance, SlowResistance, StunResistance, MarkResistance, ImpulseResistance })
                    if (!EquipmentLimits.Range(n, 0, .95f)) return false;
                return EquipmentLimits.Range(MaxHealth, 1, 10000) && EquipmentLimits.Range(DamageScale, .1f, 1)
                    && EquipmentLimits.Range(EnergyCostScale, .1f, 4)
                    && EquipmentLimits.Range(ShieldDelay, 0, 30) && EquipmentLimits.Range(RecoveryDelay, 0, 30)
                    && EquipmentLimits.Range(MoveSpeed, .1f, 20) && EquipmentLimits.Range(SprintMultiplier, 1, 5)
                    && EquipmentLimits.Range(HitInvulnerability, 0, 2) && EquipmentLimits.Range(EmergencyHealFraction, 0, .5f)
                    && EquipmentLimits.Range(EmergencyCooldown, EmergencyHealFraction > 0 ? 1 : 0, 120)
                    && (CleanseInterval == 0 || EquipmentLimits.Range(CleanseInterval, 1, 120));
            }
        }
    }
    public sealed class CompiledEquipmentPlan
    {
        public string RecipeId { get; }
        public EquipmentStats Stats { get; }
        public IReadOnlyList<EquipmentStep> Steps { get; }
        public CompiledEquipmentPlan(string recipeId, EquipmentStats stats, EquipmentStep[] steps)
        {
            if (string.IsNullOrWhiteSpace(recipeId) || !stats.IsValid || steps == null || steps.Length > EquipmentSlots.MaxOperationsPerLoadout)
                throw new ArgumentException("Invalid equipment plan.");
            foreach (var step in steps) if (!step.IsValid) throw new ArgumentException("Invalid equipment step.");
            RecipeId = recipeId; Stats = stats; Steps = Array.AsReadOnly((EquipmentStep[])steps.Clone());
        }
    }
    public readonly struct EquipmentDamageReport
    {
        public readonly float Incoming, Mitigated, ShieldLost, HealthLost;
        public readonly bool Ignored;
        public EquipmentDamageReport(float incoming, float mitigated, float shieldLost, float healthLost, bool ignored = false)
        { Incoming = incoming; Mitigated = mitigated; ShieldLost = shieldLost; HealthLost = healthLost; Ignored = ignored; }
    }
}
