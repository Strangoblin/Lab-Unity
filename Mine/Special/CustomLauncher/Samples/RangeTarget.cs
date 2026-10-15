using System.Collections.Generic;
using CelToon.CustomLauncher.Contracts;
using UnityEngine;

namespace CelToon.CustomLauncher.Samples
{
    public sealed class RangeTarget : MonoBehaviour, IDamageReceiver, IStatusReceiver, IImpulseReceiver
    {
        [SerializeField] private float maxHealth = 100f;
        public bool MoveTarget;
        private sealed class Status { public StatusInfo Info; public float Remaining; }
        private readonly Dictionary<StatusKind, Status> statuses = new Dictionary<StatusKind, Status>();
        private Renderer body; private MaterialPropertyBlock properties;
        private float flashUntil, clock; private Vector3 home, impulse;
        public float Health { get; private set; }
        public float TotalDamage { get; private set; }
        public int Hits { get; private set; }
        public int Knockdowns { get; private set; }
        public int StatusCount => statuses.Count;
        public string StatusSummary
        {
            get
            {
                if (statuses.Count == 0) return "无状态";
                var names = new List<string>();
                foreach (var kind in statuses.Keys) names.Add(kind == StatusKind.Burn ? "燃烧" : kind == StatusKind.Poison ? "中毒"
                    : kind == StatusKind.Slow ? "减速" : kind == StatusKind.Stun ? "眩晕" : "易伤");
                return string.Join("+", names);
            }
        }
        private void Awake() { properties = new MaterialPropertyBlock(); body = GetComponent<Renderer>(); home = transform.position; ResetTarget(); }
        public void ReceiveDamage(DamageInfo damage)
        {
            if (!LauncherLimits.InRange(damage.Amount, 0f, 1000000f)) return;
            float amount = damage.Amount;
            if (statuses.TryGetValue(StatusKind.Mark, out var mark)) amount *= mark.Info.Strength;
            Hits++; TotalDamage += amount; Health -= amount;
            if (Health <= 0f) { Knockdowns++; Health = maxHealth; }
            flashUntil = Time.time + .12f;
        }
        public void ReceiveStatus(StatusInfo status)
        {
            if (!LauncherLimits.InRange(status.Duration, .01f, 30f) || !LauncherLimits.InRange(status.Strength, 0f, 1000f)) return;
            if (statuses.TryGetValue(status.Kind, out var old))
            {
                float strength = status.Kind == StatusKind.Slow ? Mathf.Min(old.Info.Strength, status.Strength) : Mathf.Max(old.Info.Strength, status.Strength);
                old.Info = new StatusInfo(status.Kind, strength, Mathf.Max(old.Remaining, status.Duration), status.OwnerId, status.TeamId);
                old.Remaining = old.Info.Duration;
            }
            else statuses[status.Kind] = new Status { Info = status, Remaining = status.Duration };
        }
        public void ReceiveImpulse(Vector3 velocity) { if (LauncherLimits.Finite(velocity)) impulse += Vector3.ClampMagnitude(velocity, 30f); }
        public void ResetTarget()
        { Health = maxHealth; TotalDamage = 0f; Hits = 0; Knockdowns = 0; statuses.Clear(); impulse = Vector3.zero; clock = 0f; if (home != Vector3.zero) transform.position = home; }
        private void Update() { TickStatuses(Time.deltaTime); }
        public void TickStatuses(float dt)
        {
            var expired = new List<StatusKind>(); float speed = 1f;
            foreach (var pair in statuses)
            {
                var status = pair.Value; float step = Mathf.Min(dt, status.Remaining); status.Remaining -= step;
                if (pair.Key == StatusKind.Burn || pair.Key == StatusKind.Poison)
                    ReceiveDamage(new DamageInfo(status.Info.Strength * step, transform.position, Vector3.zero, status.Info.OwnerId, status.Info.TeamId));
                if (pair.Key == StatusKind.Slow) speed *= status.Info.Strength;
                if (pair.Key == StatusKind.Stun) speed = 0f;
                if (status.Remaining <= 0f) expired.Add(pair.Key);
            }
            foreach (var kind in expired) statuses.Remove(kind);
            clock += dt * speed; impulse *= Mathf.Exp(-4f * dt);
            if (MoveTarget) transform.position = home + Vector3.right * Mathf.Sin(clock) * 1f + impulse * .15f;
            else if (impulse.sqrMagnitude > .001f) transform.position = Vector3.Lerp(transform.position, home + impulse * .15f, Mathf.Min(1f, dt * 8f));
            else transform.position = Vector3.Lerp(transform.position, home, Mathf.Min(1f, dt * 8f));
            if (body == null) return;
            var color = statuses.ContainsKey(StatusKind.Burn) ? new Color(1f, .25f, .05f)
                : statuses.ContainsKey(StatusKind.Poison) ? Color.green : statuses.ContainsKey(StatusKind.Slow) ? Color.cyan
                : statuses.ContainsKey(StatusKind.Mark) ? Color.magenta : new Color(.9f, .35f, .15f);
            if (Time.time < flashUntil) color = Color.white;
            properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color); body.SetPropertyBlock(properties);
        }
    }
}
