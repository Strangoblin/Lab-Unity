using System;
using UnityEngine;

namespace CelToon.ProceduralController.Runtime
{
    /// <summary>L2 appendage state consumes measured attachment motion and environment observations only.</summary>
    internal sealed class SecondaryMotionPlanner
    {
        private Vector3[][] chains;
        private float cycle;
        private readonly bool[] contacts = new bool[TentacleCount];
        public float Propulsion { get; private set; }
        public int GraspingTentacles { get; private set; }
        public const int TentacleCount = 8;
        public const int TentacleSegments = 12;
        public const float TentacleLength = 0.23f;

        public bool HasContact(int arm) => contacts[arm];
        public void Reset() { chains = null; cycle = 0f; Propulsion = 0f; GraspingTentacles = 0; Array.Clear(contacts, 0, contacts.Length); }
        public void Advance(float deltaTime)
        {
            cycle = Mathf.Repeat(cycle + deltaTime * 0.85f, 1f);
            float contraction = Mathf.Max(0f, Mathf.Sin(cycle * Mathf.PI * 2f));
            Propulsion = contraction * contraction;
        }

        public Vector3[] Snake(Pose head, int segments, Vector3 velocity, float dt)
        {
            Ensure(1, segments + 1);
            var points = chains[0];
            if (points == null)
            {
                points = chains[0] = new Vector3[segments + 1];
                for (int i = 0; i < points.Length; i++) points[i] = head.position - head.rotation * Vector3.forward * (0.43f * i);
            }
            points[0] = head.position;
            float moving = Mathf.Clamp01(new Vector2(velocity.x, velocity.z).magnitude);
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 direction = points[i] - points[i - 1];
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.000001f) direction = -(head.rotation * Vector3.forward);
                Vector3 side = Vector3.Cross(Vector3.up, direction.normalized);
                Vector3 candidate = points[i] + side * (Mathf.Sin(cycle * Mathf.PI * 2f - i * 0.72f) * moving * 0.09f * dt);
                direction = candidate - points[i - 1]; direction.y = 0f;
                points[i] = points[i - 1] + direction.normalized * 0.43f;
            }
            return (Vector3[])points.Clone();
        }

        public Vector3[][] Tentacles(Pose body, bool jellyfish, GraspableObject observation, float dt)
        {
            Ensure(TentacleCount, TentacleSegments + 1);
            var output = new Vector3[TentacleCount][];
            GraspingTentacles = 0;
            float response = 1f - Mathf.Exp(-dt * (jellyfish ? 5f : 7f));
            for (int arm = 0; arm < TentacleCount; arm++)
            {
                float angle = arm * Mathf.PI * 2f / TentacleCount;
                Vector3 radial = body.rotation * new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 anchor = body.position + radial * 0.35f - Vector3.up * 0.12f;
                bool grasp = !jellyfish && observation && Vector3.Distance(anchor, observation.Center) < TentacleSegments * TentacleLength * 0.85f;
                var desired = new Vector3[TentacleSegments + 1];
                desired[0] = anchor;
                if (grasp)
                {
                    Vector3 normal = (anchor - observation.Center).normalized;
                    Vector3 tangent = Vector3.Cross(normal, Vector3.up);
                    if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.right;
                    tangent = Quaternion.AngleAxis(arm * 45f, normal) * tangent.normalized;
                    float radius = observation.Radius + 0.055f;
                    Vector3 entry = observation.Center + normal * radius;
                    float approachLength = Vector3.Distance(anchor, entry);
                    for (int i = 1; i < desired.Length; i++)
                    {
                        float distance = i * TentacleLength;
                        if (distance < approachLength) desired[i] = Vector3.Lerp(anchor, entry, distance / Mathf.Max(0.001f, approachLength));
                        else
                        {
                            // Distribute the full arm length along the approach and surface arc.
                            // A short target curve leaves slack that would otherwise form sharp folds.
                            float curl = Mathf.Min(5.5f, (distance - approachLength) / radius);
                            desired[i] = observation.Center + (normal * Mathf.Cos(curl) + tangent * Mathf.Sin(curl)) * radius;
                        }
                    }
                }
                else
                {
                    for (int i = 1; i < desired.Length; i++)
                    {
                        float t = (float)i / TentacleSegments;
                        float wave = Mathf.Sin(cycle * Mathf.PI * 2f - t * 3.2f + arm * 0.18f);
                        if (jellyfish)
                            desired[i] = anchor - Vector3.up * (i * TentacleLength * 0.82f) +
                                radial * ((0.12f + 0.22f * wave) * t + 0.42f * Propulsion * t * t) -
                                body.rotation * Vector3.forward * (Propulsion * 0.4f * t);
                        else
                            desired[i] = anchor + radial * (i * TentacleLength * 0.75f) -
                                Vector3.up * Mathf.Min(anchor.y - 0.10f, i * TentacleLength * 0.45f) +
                                Vector3.up * (0.13f * Mathf.Sin(t * Mathf.PI) * wave);
                    }
                }
                if (chains[arm] == null) chains[arm] = (Vector3[])desired.Clone();
                var points = chains[arm];
                points[0] = anchor;
                for (int i = 1; i < points.Length; i++) points[i] = Vector3.Lerp(points[i], desired[i], response);
                // Positions remain in world space across body motion. Only the attachment is imposed.
                Vector3? tip = grasp ? Vector3.Lerp(points[points.Length - 1], desired[desired.Length - 1], response) : (Vector3?)null;
                if (tip.HasValue && Vector3.Distance(tip.Value, observation.Center) < observation.Radius + 0.055f)
                    tip = observation.Center + SafeDirection(tip.Value - observation.Center) * (observation.Radius + 0.055f);
                Solve(points, anchor, tip,
                    grasp ? observation.Center : Vector3.zero, grasp ? observation.Radius + 0.035f : 0f);
                contacts[arm] = grasp && Mathf.Abs(Vector3.Distance(points[points.Length - 1], observation.Center) - observation.Radius) < 0.12f;
                if (contacts[arm]) GraspingTentacles++;
                output[arm] = (Vector3[])points.Clone();
            }
            return output;
        }

        private void Ensure(int count, int points)
        {
            if (chains == null || chains.Length != count) chains = new Vector3[count][];
            for (int i = 0; i < count; i++) if (chains[i] != null && chains[i].Length != points) chains[i] = null;
        }

        private static void Solve(Vector3[] p, Vector3 anchor, Vector3? tip, Vector3 obstacle, float radius)
        {
            // Bidirectional length and surface projections also let a bent arm retain its shape.
            for (int iteration = 0; iteration < (tip.HasValue ? 100 : 14); iteration++)
            {
                if (tip.HasValue) p[p.Length - 1] = tip.Value;
                for (int i = p.Length - 2; i >= 0; i--)
                    p[i] = p[i + 1] + SafeDirection(p[i] - p[i + 1]) * TentacleLength;
                p[0] = anchor;
                for (int i = 1; i < p.Length; i++)
                {
                    p[i] = p[i - 1] + SafeDirection(p[i] - p[i - 1]) * TentacleLength;
                    if (radius > 0f && Vector3.Distance(p[i], obstacle) < radius)
                        p[i] = obstacle + SafeDirection(p[i] - obstacle) * radius;
                }
            }
        }

        private static Vector3 SafeDirection(Vector3 delta) => delta.sqrMagnitude > 0.000001f ? delta.normalized : Vector3.down;
    }
}
