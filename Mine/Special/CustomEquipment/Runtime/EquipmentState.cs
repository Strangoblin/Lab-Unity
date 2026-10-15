using System;
using System.Collections.Generic;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomLauncher.Contracts;
using UnityEngine;

namespace CelToon.CustomEquipment.Runtime
{
    public sealed class EquipmentState
    {
        private sealed class ActiveStatus { public float Strength, Remaining; }
        private readonly Dictionary<StatusKind, ActiveStatus> statuses = new Dictionary<StatusKind, ActiveStatus>();
        private readonly List<StatusKind> expired = new List<StatusKind>();
        private float quietTime, invulnerability, emergencyCooldown, cleanseClock;
        private bool exhausted;
        public CompiledEquipmentPlan Plan { get; private set; }
        public float Health { get; private set; }
        public float Shield { get; private set; }
        public float Stamina { get; private set; }
        public float Energy { get; private set; }
        public float TotalIncoming { get; private set; }
        public float TotalMitigated { get; private set; }
        public float TotalShieldLost { get; private set; }
        public float TotalHealthLost { get; private set; }
        public int Hits { get; private set; }
        public int EmergencyActivations { get; private set; }
        public bool Sprinting { get; private set; }
        public bool IsDead => Plan != null && Health <= 0;
        public bool IsInvulnerable => invulnerability > 0;
        public int StatusCount => statuses.Count;
        public EquipmentDamageReport LastDamage { get; private set; }
        public float StatusRemaining(StatusKind kind) => statuses.TryGetValue(kind, out var status) ? status.Remaining : 0;
        public float MovementFactor => IsDead || statuses.ContainsKey(StatusKind.Stun) ? 0
            : statuses.TryGetValue(StatusKind.Slow, out var slow) ? 1 - (1 - slow.Strength) * (1 - Plan.Stats.SlowResistance) : 1;
        public float MoveSpeed => Plan == null ? 0 : Plan.Stats.MoveSpeed * MovementFactor * (Sprinting ? Plan.Stats.SprintMultiplier : 1);
        public string StatusSummary
        {
            get
            {
                if (statuses.Count == 0) return "无状态";
                var names = new List<string>();
                foreach (var kind in statuses.Keys) names.Add((kind == StatusKind.Burn ? "燃烧" : kind == StatusKind.Poison ? "中毒"
                    : kind == StatusKind.Slow ? "减速" : kind == StatusKind.Stun ? "眩晕" : "易伤") + " " + StatusRemaining(kind).ToString("F1") + "s");
                return string.Join(" / ", names);
            }
        }
        public void Equip(CompiledEquipmentPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            bool initial = Plan == null; Plan = plan;
            if (initial) Reset();
            else
            {
                // Preserve current resources and every timer. Increasing a cap cannot grant free health or shield.
                Health = Math.Min(Health, plan.Stats.MaxHealth); Shield = Math.Min(Shield, plan.Stats.MaxShield);
                Stamina = Math.Min(Stamina, plan.Stats.MaxStamina);
                Energy = Math.Min(Energy, plan.Stats.MaxEnergy);
            }
        }
        public void Reset()
        {
            if (Plan == null) return;
            Health = Plan.Stats.MaxHealth; Shield = Plan.Stats.MaxShield; Stamina = Plan.Stats.MaxStamina;
            Energy = Plan.Stats.MaxEnergy;
            quietTime = invulnerability = emergencyCooldown = cleanseClock = 0;
            TotalIncoming = TotalMitigated = TotalShieldLost = TotalHealthLost = 0;
            Hits = EmergencyActivations = 0; Sprinting = exhausted = false; statuses.Clear(); LastDamage = default;
        }
        // Consumes a caller-supplied base cost. No dependency on weapon plans, emitters or firing.
        public bool TrySpendEnergy(float baseCost)
        {
            if (Plan == null || IsDead || !EquipmentLimits.Range(baseCost, 0, 1000000)) return false;
            float cost = baseCost * Plan.Stats.EnergyCostScale;
            if (!EquipmentLimits.Range(cost, 0, 4000000) || Energy < cost) return false;
            Energy -= cost; return true;
        }
        public bool RechargeEnergy(float amount)
        {
            if (Plan == null || IsDead || !EquipmentLimits.Range(amount, 0, 1000000)) return false;
            Energy = Math.Min(Plan.Stats.MaxEnergy, Energy + amount); return true;
        }
        public EquipmentDamageReport ApplyDamage(float amount, DamageChannel channel = DamageChannel.Direct)
        {
            if (Plan == null || IsDead || !EquipmentLimits.Range(amount, 0, 1000000) || amount == 0
                || !Enum.IsDefined(typeof(DamageChannel), channel) || (channel == DamageChannel.Direct && IsInvulnerable))
                return new EquipmentDamageReport(0, 0, 0, 0, true);
            var stats = Plan.Stats;
            float incoming = amount;
            if (statuses.TryGetValue(StatusKind.Mark, out var mark)) incoming *= 1 + (mark.Strength - 1) * (1 - stats.MarkResistance);
            float effective = channel == DamageChannel.Direct ? Mathf.Max(0, incoming - stats.Armor)
                : incoming * (1 - (channel == DamageChannel.Burn ? stats.BurnResistance : stats.PoisonResistance));
            effective *= stats.DamageScale;
            float shieldLost = Mathf.Min(Shield, effective), healthLost = Mathf.Min(Health, effective - shieldLost);
            Shield -= shieldLost; Health -= healthLost;
            if (effective > 0) quietTime = 0;
            if (channel == DamageChannel.Direct)
            { Hits++; if (effective > 0) invulnerability = Mathf.Max(invulnerability, stats.HitInvulnerability); }
            TotalIncoming += incoming; TotalMitigated += incoming - effective;
            TotalShieldLost += shieldLost; TotalHealthLost += healthLost;
            if (healthLost > 0 && Health > 0 && Health <= stats.MaxHealth * .3f && stats.EmergencyHealFraction > 0 && emergencyCooldown <= 0)
            {
                Health = Mathf.Min(stats.MaxHealth, Health + stats.MaxHealth * stats.EmergencyHealFraction);
                emergencyCooldown = stats.EmergencyCooldown; EmergencyActivations++;
            }
            LastDamage = new EquipmentDamageReport(incoming, incoming - effective, shieldLost, healthLost);
            return LastDamage;
        }
        public bool ApplyStatus(StatusInfo info)
        {
            if (Plan == null || IsDead || !Enum.IsDefined(typeof(StatusKind), info.Kind)
                || !EquipmentLimits.Range(info.Duration, .01f, 30) || !EquipmentLimits.Range(info.Strength, 0, 1000)
                || (info.Kind == StatusKind.Slow && !EquipmentLimits.Range(info.Strength, 0, 1))
                || (info.Kind == StatusKind.Mark && !EquipmentLimits.Range(info.Strength, 1, 5))) return false;
            float duration = info.Duration * (info.Kind == StatusKind.Stun ? 1 - Plan.Stats.StunResistance : 1);
            if (statuses.TryGetValue(info.Kind, out var previous))
            {
                previous.Strength = info.Kind == StatusKind.Slow ? Mathf.Min(previous.Strength, info.Strength) : Mathf.Max(previous.Strength, info.Strength);
                previous.Remaining = Mathf.Max(previous.Remaining, duration);
            }
            else statuses.Add(info.Kind, new ActiveStatus { Strength = info.Strength, Remaining = duration });
            return true;
        }
        public void Tick(float dt, bool sprintRequested = false)
        {
            if (Plan == null || !EquipmentLimits.Range(dt, 0, 10)) return;
            // Same bounded substep for DOT, recovery delays and reaction timers, independent of frame rate.
            while (dt > 0)
            {
                float step = Mathf.Min(dt, .02f); Step(step, sprintRequested); dt -= step;
            }
        }
        private void Step(float dt, bool sprintRequested)
        {
            if (IsDead) { Sprinting = false; return; }
            var stats = Plan.Stats;
            invulnerability = Mathf.Max(0, invulnerability - dt); emergencyCooldown = Mathf.Max(0, emergencyCooldown - dt);
            quietTime += dt;
            expired.Clear();
            foreach (var pair in statuses)
            {
                float activeTime = Mathf.Min(dt, pair.Value.Remaining);
                if (pair.Key == StatusKind.Burn || pair.Key == StatusKind.Poison)
                    ApplyDamage(pair.Value.Strength * activeTime, pair.Key == StatusKind.Burn ? DamageChannel.Burn : DamageChannel.Poison);
                pair.Value.Remaining -= dt;
                if (pair.Value.Remaining <= .00001f) expired.Add(pair.Key);
            }
            foreach (var kind in expired) statuses.Remove(kind);
            if (IsDead) { Sprinting = false; return; }
            Energy = Mathf.Min(stats.MaxEnergy, Energy + stats.EnergyRegen * dt);
            if (quietTime > stats.ShieldDelay)
                Shield = Mathf.Min(stats.MaxShield, Shield + stats.ShieldRegen * Mathf.Min(dt, quietTime - stats.ShieldDelay));
            if (quietTime > stats.RecoveryDelay)
                Health = Mathf.Min(stats.MaxHealth, Health + stats.HealthRegen * Mathf.Min(dt, quietTime - stats.RecoveryDelay));
            if (exhausted && Stamina >= stats.MaxStamina * .25f && Stamina > 0) exhausted = false;
            Sprinting = sprintRequested && MovementFactor > 0 && !exhausted && Stamina > 0;
            if (Sprinting) { Stamina = Mathf.Max(0, Stamina - stats.SprintCost * dt); if (Stamina <= 0) exhausted = true; }
            else Stamina = Mathf.Min(stats.MaxStamina, Stamina + stats.StaminaRegen * dt);
            cleanseClock += dt;
            if (stats.CleanseInterval > 0 && cleanseClock >= stats.CleanseInterval)
            { statuses.Clear(); cleanseClock %= stats.CleanseInterval; }
        }
    }
}
