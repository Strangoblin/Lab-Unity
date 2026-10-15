using System;
using System.Collections.Generic;
using CelToon.CustomLauncher.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace CelToon.CustomLauncher.Projectiles
{
    public sealed class ProjectilePool : MonoBehaviour, IProjectileEmitter
    {
        [SerializeField, Range(1, 4096)] private int capacity = 512;
        [SerializeField] private LayerMask collisionMask = ~0;
        private sealed class Budget { public int Used, Limit; }
        private sealed class Entry
        {
            public GameObject Visual; public Renderer Renderer; public TrailRenderer Trail;
            public ProjectileCommand Command; public Vector3 Position, Velocity, Right, Up;
            public float Age, Delay; public uint Triggered; public int Bounces, Pierces;
            public bool Child; public Budget Budget;
            public Collider LastBounce;
            public readonly HashSet<int> Contacts = new HashSet<int>();
        }
        private readonly struct Pending
        {
            public readonly ProjectileCommand Command; public readonly Budget Budget;
            public Pending(ProjectileCommand command, Budget budget) { Command = command; Budget = budget; }
        }
        private readonly List<Entry> active = new List<Entry>();
        private readonly Stack<Entry> idle = new Stack<Entry>();
        private readonly List<Pending> pending = new List<Pending>();
        private MaterialPropertyBlock properties;
        private Material material, trailMaterial;
        public int ActiveCount => active.Count;
        public int CreatedCount { get; private set; }
        public int Capacity => capacity;
        public int HitCount { get; private set; }
        public int TriggerCount { get; private set; }
        public int GeneratedCount { get; private set; }
        public int RejectedTriggers { get; private set; }
        private void Awake()
        {
            properties = new MaterialPropertyBlock();
            var shader = Shader.Find(GraphicsSettings.currentRenderPipeline != null ? "Universal Render Pipeline/Lit" : "Standard");
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
            material = new Material(shader) { name = "CustomLauncher_Projectiles" };
            var trailShader = Shader.Find(GraphicsSettings.currentRenderPipeline != null ? "Universal Render Pipeline/Particles/Unlit" : "Sprites/Default");
            trailMaterial = new Material(trailShader != null ? trailShader : shader) { name = "CustomLauncher_Trails" };
        }
        public void ConfigureCapacity(int value)
        {
            if (value < 1 || value > 4096 || active.Count != 0 || CreatedCount != 0) throw new InvalidOperationException("Configure capacity before emitting (1..4096).");
            capacity = value;
        }
        public bool TryEmit(IReadOnlyList<ProjectileCommand> commands)
        {
            if (!isActiveAndEnabled || commands == null || commands.Count == 0 || commands.Count > capacity - active.Count) return false;
            int limit = LauncherLimits.MaxGeneratedPerShot;
            foreach (var command in commands)
            {
                if (!command.Spec.IsValid || !LauncherLimits.Finite(command.Origin) || !LauncherLimits.InRange(command.Origin.magnitude, 0f, 1000000f)
                    || !LauncherLimits.Finite(command.Direction) || !LauncherLimits.InRange(command.Direction.magnitude, .999f, 1.001f)
                    || !LauncherLimits.InRange(command.Delay, 0f, 16f) || command.Program == null) return false;
                limit = Math.Min(limit, command.Program.MaxGenerated);
            }
            if (commands.Count > limit) return false;
            var budget = new Budget { Used = commands.Count, Limit = limit };
            foreach (var command in commands) Spawn(command, budget, false);
            return true;
        }
        private void Spawn(ProjectileCommand command, Budget budget, bool child)
        {
            var entry = idle.Count > 0 ? idle.Pop() : CreateEntry();
            entry.Command = command; entry.Position = command.Origin; entry.Velocity = command.Direction * command.Spec.Speed;
            entry.Age = 0f; entry.Delay = command.Delay; entry.Triggered = 0; entry.Child = child; entry.Budget = budget;
            entry.Contacts.Clear(); entry.Bounces = 0; entry.Pierces = 0;
            entry.LastBounce = null;
            foreach (var step in command.Program.Steps)
            { if (step.Operation == EffectOperation.Bounce) entry.Bounces += step.Count; if (step.Operation == EffectOperation.Pierce) entry.Pierces += step.Count; }
            var axis = Mathf.Abs(Vector3.Dot(command.Direction, Vector3.up)) > .95f ? Vector3.right : Vector3.up;
            entry.Right = Vector3.Cross(axis, command.Direction).normalized; entry.Up = Vector3.Cross(command.Direction, entry.Right).normalized;
            entry.Visual.transform.position = entry.Position; entry.Visual.transform.localScale = Vector3.one * (command.Spec.Radius * 2f);
            properties.Clear(); properties.SetColor("_BaseColor", command.Spec.Color); properties.SetColor("_Color", command.Spec.Color);
            entry.Renderer.SetPropertyBlock(properties); entry.Trail.Clear(); entry.Trail.startColor = command.Spec.Color;
            entry.Trail.endColor = new Color(command.Spec.Color.r, command.Spec.Color.g, command.Spec.Color.b, 0f);
            entry.Trail.startWidth = command.Spec.Radius; entry.Trail.endWidth = .005f;
            entry.Visual.SetActive(command.Delay <= 0f); active.Add(entry); GeneratedCount++;
        }
        private Entry CreateEntry()
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere); visual.name = "PooledProjectile"; visual.transform.SetParent(transform, true);
            var collider = visual.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            var renderer = visual.GetComponent<Renderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            var trail = visual.AddComponent<TrailRenderer>(); trail.sharedMaterial = trailMaterial; trail.time = .16f;
            trail.minVertexDistance = .05f; trail.shadowCastingMode = ShadowCastingMode.Off;
            CreatedCount++; return new Entry { Visual = visual, Renderer = renderer, Trail = trail };
        }
        private void Update() { Tick(Time.deltaTime); }
        public void Tick(float deltaTime)
        {
            if (!LauncherLimits.InRange(deltaTime, 0f, 10f)) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            // Substeps maintain curved trajectories and contact order during low frame rates.
            while (deltaTime > .000001f)
            {
                float dt = Mathf.Min(.02f, deltaTime); deltaTime -= dt;
                for (int i = active.Count - 1; i >= 0; i--)
                {
                    var entry = active[i]; float stepTime = dt;
                    if (entry.LastBounce != null && (entry.LastBounce.ClosestPoint(entry.Position) - entry.Position).magnitude > entry.Command.Spec.Radius + .08f)
                        entry.LastBounce = null;
                    if (entry.Delay > 0f)
                    { float wait = Mathf.Min(entry.Delay, stepTime); entry.Delay -= wait; stepTime -= wait;
                        if (entry.Delay > 0f || stepTime <= 0f) continue; entry.Visual.SetActive(true); entry.Trail.Clear(); }
                    stepTime = Mathf.Min(stepTime, entry.Command.Spec.Lifetime - entry.Age);
                    var destination = Move(entry, stepTime); entry.Age += stepTime;
                    bool terminated = false;
                    for (int contactIndex = 0; contactIndex < 129 && TryContact(entry, destination, out var contact, out var point, out var normal); contactIndex++)
                    {
                        var motion = destination - entry.Position;
                        float remaining = Mathf.Max(0f, motion.magnitude - Vector3.Distance(entry.Position, point) - entry.Command.Spec.Radius);
                        entry.Position = point + normal * (entry.Command.Spec.Radius + .015f);
                        HitCount++;
                        ApplyHit(entry, contact, point); QueueTriggers(entry, EffectOperation.HitSplit);
                        if (entry.Pierces > 0)
                        { entry.Contacts.Add(contact.GetInstanceID()); entry.Pierces--; entry.Position = point + motion.normalized * (entry.Command.Spec.Radius + .015f); }
                        else if (entry.Bounces > 0)
                        { entry.LastBounce = contact; entry.Bounces--; entry.Velocity = Vector3.Reflect(entry.Velocity, normal); destination = entry.Position + entry.Velocity.normalized * remaining; }
                        else { RecycleAt(i); terminated = true; break; }
                    }
                    if (terminated) continue;
                    entry.Position = destination;
                    QueueTriggers(entry, EffectOperation.TimedSplit);
                    if (entry.Age + .00001f >= entry.Command.Spec.Lifetime)
                    { QueueTriggers(entry, EffectOperation.ExpirySplit); RecycleAt(i); continue; }
                    entry.Visual.transform.position = entry.Position;
                }
                FlushPending();
            }
        }
        private Vector3 Move(Entry entry, float dt)
        {
            var offset = Vector3.zero; var velocity = entry.Velocity; float nextAge = entry.Age + dt;
            foreach (var step in entry.Command.Program.Steps)
            {
                switch (step.Operation)
                {
                    case EffectOperation.Gravity: velocity += Vector3.down * step.A * dt; break;
                    case EffectOperation.Acceleration: velocity += velocity.normalized * step.A * dt; break;
                    case EffectOperation.Drag: velocity *= Mathf.Exp(-step.A * dt); break;
                    case EffectOperation.Homing:
                        var target = NearestTarget(entry.Position, step.B, entry.Command.OwnerId, entry.Contacts);
                        if (target != null) velocity = Vector3.RotateTowards(velocity, (target.bounds.center - entry.Position).normalized * velocity.magnitude, step.A * dt, 0f);
                        break;
                    case EffectOperation.Return:
                        if (nextAge >= step.Delay) velocity = Vector3.RotateTowards(velocity,
                            (entry.Command.Origin - entry.Position).normalized * velocity.magnitude, step.A * dt, 0f);
                        break;
                    case EffectOperation.Wave:
                        offset += entry.Right * step.A * (Mathf.Sin(nextAge * step.B) - Mathf.Sin(entry.Age * step.B)); break;
                    case EffectOperation.Helix:
                        offset += entry.Right * step.A * (Mathf.Sin(nextAge * step.B) - Mathf.Sin(entry.Age * step.B))
                            + entry.Up * step.A * (Mathf.Cos(nextAge * step.B) - Mathf.Cos(entry.Age * step.B)); break;
                }
            }
            entry.Velocity = Vector3.ClampMagnitude(velocity, 1000f);
            return entry.Position + entry.Velocity * dt + offset;
        }
        private bool TryContact(Entry entry, Vector3 destination, out Collider contact, out Vector3 point, out Vector3 normal)
        {
            contact = null; point = entry.Position; normal = -entry.Velocity.normalized; float closest = float.PositiveInfinity;
            foreach (var collider in Physics.OverlapSphere(entry.Position, entry.Command.Spec.Radius, collisionMask, QueryTriggerInteraction.Ignore))
            {
                if (Ignored(entry, collider)) continue;
                var candidate = collider.ClosestPoint(entry.Position); float distance = (candidate - entry.Position).sqrMagnitude;
                if (distance < closest) { closest = distance; contact = collider; point = candidate; }
            }
            if (contact != null) return true;
            var delta = destination - entry.Position;
            if (delta.sqrMagnitude <= .00000001f) return false;
            foreach (var hit in Physics.SphereCastAll(entry.Position, entry.Command.Spec.Radius, delta.normalized,
                delta.magnitude, collisionMask, QueryTriggerInteraction.Ignore))
                if (hit.distance < closest && !Ignored(entry, hit.collider))
                { closest = hit.distance; contact = hit.collider; point = hit.point; normal = hit.normal; }
            return contact != null;
        }
        private static bool IsOwner(Transform target, int ownerId)
        {
            if (ownerId == 0) return false;
            for (var cursor = target; cursor != null; cursor = cursor.parent) if (cursor.gameObject.GetInstanceID() == ownerId) return true;
            return false;
        }
        private static bool Ignored(Entry entry, Collider collider) => IsOwner(collider.transform, entry.Command.OwnerId)
            || entry.Contacts.Contains(collider.GetInstanceID()) || entry.LastBounce == collider;
        private Collider NearestTarget(Vector3 position, float radius, int owner, HashSet<int> excluded, HashSet<IDamageReceiver> alreadyHit = null)
        {
            Collider result = null; float closest = float.PositiveInfinity;
            foreach (var collider in Physics.OverlapSphere(position, radius, collisionMask, QueryTriggerInteraction.Ignore))
            {
                if (IsOwner(collider.transform, owner) || (excluded != null && excluded.Contains(collider.GetInstanceID()))
                    || collider.GetComponentInParent<IDamageReceiver>() == null
                    || (alreadyHit != null && alreadyHit.Contains(collider.GetComponentInParent<IDamageReceiver>()))) continue;
                float distance = (collider.bounds.center - position).sqrMagnitude;
                if (distance < closest) { closest = distance; result = collider; }
            }
            return result;
        }
        private void ApplyHit(Entry entry, Collider contact, Vector3 point)
        {
            var command = entry.Command; var direction = entry.Velocity.normalized;
            var receiver = contact.GetComponentInParent<IDamageReceiver>();
            receiver?.ReceiveDamage(new DamageInfo(command.Spec.Damage, point, direction, command.OwnerId, command.TeamId));
            foreach (var step in command.Program.Steps)
            {
                switch (step.Operation)
                {
                    case EffectOperation.BonusDamage: receiver?.ReceiveDamage(new DamageInfo(command.Spec.Damage * step.A, point, direction, command.OwnerId, command.TeamId)); break;
                    case EffectOperation.Burn: case EffectOperation.Poison: case EffectOperation.Slow: case EffectOperation.Stun: case EffectOperation.Mark:
                        var kind = step.Operation == EffectOperation.Burn ? StatusKind.Burn : step.Operation == EffectOperation.Poison ? StatusKind.Poison
                            : step.Operation == EffectOperation.Slow ? StatusKind.Slow : step.Operation == EffectOperation.Stun ? StatusKind.Stun : StatusKind.Mark;
                        contact.GetComponentInParent<IStatusReceiver>()?.ReceiveStatus(new StatusInfo(kind, step.A, step.B, command.OwnerId, command.TeamId)); break;
                    case EffectOperation.Explosion:
                        var damaged = new HashSet<IDamageReceiver>();
                        foreach (var collider in Physics.OverlapSphere(point, step.A, collisionMask, QueryTriggerInteraction.Ignore))
                        {
                            if (IsOwner(collider.transform, command.OwnerId)) continue;
                            var victim = collider.GetComponentInParent<IDamageReceiver>();
                            if (victim != null && damaged.Add(victim)) victim.ReceiveDamage(new DamageInfo(command.Spec.Damage * step.B,
                                point, (collider.bounds.center - point).normalized, command.OwnerId, command.TeamId));
                        }
                        break;
                    case EffectOperation.Chain:
                        var excluded = new HashSet<int>(entry.Contacts) { contact.GetInstanceID() }; var start = point;
                        var chained = new HashSet<IDamageReceiver>(); if (receiver != null) chained.Add(receiver);
                        for (int i = 0; i < step.Count; i++)
                        {
                            var next = NearestTarget(start, step.A, command.OwnerId, excluded, chained); if (next == null) break;
                            excluded.Add(next.GetInstanceID()); var nextReceiver = next.GetComponentInParent<IDamageReceiver>(); chained.Add(nextReceiver); nextReceiver.ReceiveDamage(
                                new DamageInfo(command.Spec.Damage * step.B, next.bounds.center, direction, command.OwnerId, command.TeamId)); start = next.bounds.center;
                        }
                        break;
                    case EffectOperation.Push: case EffectOperation.Pull:
                        var impulse = direction * step.A * (step.Operation == EffectOperation.Pull ? -1f : 1f);
                        var response = contact.GetComponentInParent<IImpulseReceiver>();
                        if (response != null) response.ReceiveImpulse(impulse);
                        else if (contact.attachedRigidbody != null && !contact.attachedRigidbody.isKinematic) contact.attachedRigidbody.AddForce(impulse, ForceMode.VelocityChange);
                        break;
                }
            }
        }
        private void QueueTriggers(Entry entry, EffectOperation operation)
        {
            if (entry.Child) return; // Children inherit other effects, but never recursively inherit splitting.
            var steps = entry.Command.Program.Steps;
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i]; uint bit = 1u << i;
                if (step.Operation != operation || (entry.Triggered & bit) != 0 || (operation == EffectOperation.TimedSplit && entry.Age + .00001f < step.Delay)) continue;
                entry.Triggered |= bit;
                if (entry.Budget.Used + step.Count > entry.Budget.Limit || active.Count + pending.Count + step.Count > capacity)
                { RejectedTriggers++; continue; }
                entry.Budget.Used += step.Count; TriggerCount++;
                var forward = entry.Velocity.sqrMagnitude > .0001f ? entry.Velocity.normalized : entry.Command.Direction;
                var axis = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .95f ? Vector3.right : Vector3.up;
                var spec = entry.Command.Spec;
                var childSpec = new ProjectileSpec(spec.Damage * .65f, Mathf.Clamp(spec.Speed * step.B, .1f, 1000f), spec.Radius * .7f < .01f ? .01f : spec.Radius * .7f,
                    spec.Lifetime, spec.Color);
                for (int child = 0; child < step.Count; child++)
                {
                    float angle = step.Count > 1 ? -step.A * .5f + step.A * child / (step.Count - 1) : 0f;
                    var direction = (Quaternion.AngleAxis(angle, axis) * forward).normalized;
                    pending.Add(new Pending(new ProjectileCommand(entry.Position + direction * (spec.Radius + .05f), direction, childSpec,
                        entry.Command.OwnerId, entry.Command.TeamId, entry.Command.Program), entry.Budget));
                }
            }
        }
        private void FlushPending() { foreach (var child in pending) Spawn(child.Command, child.Budget, true); pending.Clear(); }
        private void RecycleAt(int index)
        {
            var entry = active[index]; active.RemoveAt(index); entry.Trail.Clear(); entry.Visual.SetActive(false);
            entry.Command = default; entry.Budget = null; entry.Contacts.Clear(); idle.Push(entry);
        }
        public void Clear()
        { pending.Clear(); for (int i = active.Count - 1; i >= 0; i--) RecycleAt(i); TriggerCount = 0; GeneratedCount = 0; RejectedTriggers = 0; }
        private void OnDisable() { Clear(); }
        private void OnDestroy() { if (material != null) Destroy(material); if (trailMaterial != null) Destroy(trailMaterial); }
    }
}
