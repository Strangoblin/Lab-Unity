using CelToon.PathGenerator.Contracts;
using System;
using UnityEngine;
using CelToon.ProceduralController.Contracts;

namespace CelToon.ProceduralController.Runtime
{
    public enum TestMorphology { Quadruped, Hexapod, Octopod, Snake, Humanoid, Jellyfish, Octopus }

    /// <summary>L2 turns a route and measured rig data into timed module trajectories.</summary>
    public sealed class ProceduralAnimationManager : MonoBehaviour, IProceduralPlanner
    {
        [SerializeField] private TestMorphology morphology;
        [SerializeField, Min(0.1f)] private float moveSpeed = 2f;
        [SerializeField, Min(0.1f)] private float acceleration = 3f;
        [SerializeField, Min(0.1f)] private float deceleration = 5f;
        [SerializeField, Min(0.1f)] private float strideFrequency = 1f;
        [SerializeField] private bool coupleSpeedToStride = true;
        [SerializeField, Range(1f, 4f)] private float turnStrideMultiplier = 3f;
        [SerializeField, Min(0f)] private float stepHeight = 0.17f;
        [SerializeField, Min(0f)] private float stoppingDistance = 0.2f;
        private IGroundProbe ground;
        private Vector3[] footAnchors = Array.Empty<Vector3>();
        private Vector3[] swingOrigins = Array.Empty<Vector3>();
        private Vector3[] swingGoals = Array.Empty<Vector3>();
        private Quaternion[] footRotations = Array.Empty<Quaternion>();
        private bool[] wasSwing = Array.Empty<bool>();
        private bool[] hasAnchor = Array.Empty<bool>();
        private float[] swingProgress = Array.Empty<float>();
        private bool[] stepWindowConsumed = Array.Empty<bool>();
        private bool[] swingUsesPrediction = Array.Empty<bool>();
        private int spineCount;
        private uint sequence;
        private uint trackedRouteVersion;
        private int waypointIndex = 1;
        private float gaitClock;
        private Vector3 driveVelocity;
        private Quaternion desiredFacing = Quaternion.identity;
        private float turnDemand;
        private float effectiveFrequency;
        private readonly SecondaryMotionPlanner secondary = new SecondaryMotionPlanner();
        public int GraspingTentacles => secondary.GraspingTentacles;
        public float Propulsion => secondary.Propulsion;
        public float MoveSpeed { get => moveSpeed; set => moveSpeed = Mathf.Max(.1f, value); }
        public float Acceleration { get => acceleration; set => acceleration = Mathf.Max(.1f, value); }
        public float Deceleration { get => deceleration; set => deceleration = Mathf.Max(.1f, value); }
        public float StoppingDistance { get => stoppingDistance; set => stoppingDistance = Mathf.Max(0f, value); }
        public float StrideFrequency { get => strideFrequency; set => strideFrequency = Mathf.Max(.1f, value); }
        public float StepHeight { get => stepHeight; set => stepHeight = Mathf.Max(0f, value); }
        public float TurnStrideMultiplier { get => turnStrideMultiplier; set => turnStrideMultiplier = Mathf.Clamp(value, 1f, 4f); }
        public float EffectiveStrideFrequency => effectiveFrequency;
        public bool CoupleSpeedToStride { get => coupleSpeedToStride; set => coupleSpeedToStride = value; }
        public bool UsesLegGait => morphology == TestMorphology.Quadruped || morphology == TestMorphology.Hexapod ||
            morphology == TestMorphology.Octopod || morphology == TestMorphology.Humanoid;
        public float GaitSpeedLimit => coupleSpeedToStride && UsesLegGait ?
            Mathf.Min(moveSpeed * MotionFitProfile.SpeedScale(morphology) * strideFrequency,
                4f * MotionFitProfile.ReferenceCycleDistance(morphology)) : moveSpeed * MotionFitProfile.SpeedScale(morphology);
        public Vector3 PlannedVelocityWS => driveVelocity;

        public void Configure(TestMorphology shape, IGroundProbe probe, int segments = 0)
        { morphology = shape; ground = probe; spineCount = Mathf.Max(0, segments); Reset(new PlannerResetContext()); }

        public void Reset(in PlannerResetContext context)
        {
            footAnchors = Array.Empty<Vector3>();
            swingOrigins = Array.Empty<Vector3>();
            swingGoals = Array.Empty<Vector3>();
            footRotations = Array.Empty<Quaternion>();
            wasSwing = Array.Empty<bool>();
            hasAnchor = Array.Empty<bool>();
            swingProgress = Array.Empty<float>();
            stepWindowConsumed = Array.Empty<bool>();
            swingUsesPrediction = Array.Empty<bool>();
            sequence = 0;
            trackedRouteVersion = 0;
            waypointIndex = 1;
            gaitClock = 0f;
            driveVelocity = Vector3.zero;
            turnDemand = 0f;
            effectiveFrequency = Mathf.Min(4f, MotionFitProfile.Frequency(morphology) * strideFrequency);
            secondary.Reset();
        }

        public RootMotionCommand PlanRoot(in PlannerInput input)
        {
            secondary.Advance(input.Tick.DeltaTime);
            desiredFacing = input.Character.RootPoseWS.rotation;
            turnDemand = 0f;
            var cmd = new RootMotionCommand
            {
                ActorId = input.Tick.ActorId, Epoch = input.Tick.Epoch, TickId = input.Tick.TickId,
                TargetRotationWS = input.Character.RootPoseWS.rotation, MaxAngularSpeed = morphology == TestMorphology.Snake ? 100f : 180f,
                ValidUntil = input.Tick.Now + 0.2d
            };
            if (input.Route == null || input.Route.Status != RouteStatus.Complete || input.Route.Waypoints.Length < 2)
            {
                driveVelocity = Vector3.MoveTowards(driveVelocity, Vector3.zero, input.Tick.DeltaTime * Mathf.Max(.1f, deceleration));
                cmd.DeltaPositionWS = driveVelocity * input.Tick.DeltaTime;
                return ConstrainToSupport(input, cmd);
            }
            if (trackedRouteVersion != input.Route.RouteVersion)
            { trackedRouteVersion = input.Route.RouteVersion; waypointIndex = 1; }
            while (waypointIndex < input.Route.Waypoints.Length - 1)
            {
                Vector3 nextPoint = input.Route.Waypoints[waypointIndex].PositionWS - input.Character.RootPoseWS.position;
                nextPoint.y = 0f;
                if (nextPoint.magnitude > 0.35f) break;
                waypointIndex++;
            }
            Vector3 goal = input.Route.Waypoints[waypointIndex].PositionWS;
            Vector3 difference = goal - input.Character.RootPoseWS.position;
            difference.y = 0f;
            float distance = difference.magnitude;
            float stop = waypointIndex == input.Route.Waypoints.Length - 1 ? stoppingDistance : 0.1f;
            Vector3 direction = distance > .00001f ? difference / distance : Vector3.zero;
            float propulsion = morphology == TestMorphology.Jellyfish ? 0.25f + 1.5f * secondary.Propulsion : 1f;
            float rigSpeed = GaitSpeedLimit;
            float remaining = Mathf.Max(0f, distance - stop);
            float brake = Mathf.Max(.1f, deceleration);
            // Discrete-time stopping limit, including the next integration step. Only the
            // final waypoint is a stop: intermediate route points remain pass-through.
            float brakeStep = brake * input.Tick.DeltaTime;
            float speedLimit = waypointIndex == input.Route.Waypoints.Length - 1 ?
                Mathf.Sqrt(brakeStep * brakeStep + 2f * brake * remaining) - brakeStep : float.PositiveInfinity;
            Vector3 desired = direction * Mathf.Min(rigSpeed * propulsion, speedLimit);
            bool braking = desired.sqrMagnitude < driveVelocity.sqrMagnitude || Vector3.Dot(desired, driveVelocity) < 0f;
            driveVelocity = Vector3.MoveTowards(driveVelocity, desired,
                input.Tick.DeltaTime * (braking ? brake : Mathf.Max(.1f, acceleration)));
            cmd.DeltaPositionWS = driveVelocity * input.Tick.DeltaTime;
            // A newly retargeted goal may already be inside the stopping zone.
            if (waypointIndex == input.Route.Waypoints.Length - 1)
                cmd.DeltaPositionWS = Vector3.ClampMagnitude(cmd.DeltaPositionWS, remaining);
            if (direction.sqrMagnitude > .0001f && remaining > .001f)
                cmd.TargetRotationWS = Quaternion.LookRotation(direction, Vector3.up);
            desiredFacing = cmd.TargetRotationWS;
            turnDemand = Mathf.Clamp01(Quaternion.Angle(input.Character.RootPoseWS.rotation, desiredFacing) / 60f);
            return ConstrainToSupport(input, cmd);
        }

        // A planted foot is a world constraint. Limit body translation AND rotation before
        // moving the driver, rather than letting the IK reach clamp drag the foot along.
        private RootMotionCommand ConstrainToSupport(in PlannerInput input, RootMotionCommand command)
        {
            var metrics = input.Metrics.Modules;
            if (metrics == null || footAnchors.Length != metrics.Length || metrics.Length == 0) return command;
            Pose start = input.Character.RootPoseWS;
            Quaternion endRotation = Quaternion.RotateTowards(start.rotation, command.TargetRotationWS,
                command.MaxAngularSpeed * input.Tick.DeltaTime);
            float fraction = 1f;
            for (int i = 0; i < metrics.Length; i++)
            {
                if (!hasAnchor[i] || wasSwing[i]) continue;
                float reach = metrics[i].UpperLength + metrics[i].LowerLength - .006f;
                float low = 0f, high = fraction;
                for (int iteration = 0; iteration < 12; iteration++)
                {
                    float test = (low + high) * .5f;
                    Quaternion rotation = Quaternion.Slerp(start.rotation, endRotation, test);
                    Vector3 hip = start.position + command.DeltaPositionWS * test +
                        Vector3.up * (input.Metrics.BodyHeight + .04f) + rotation * metrics[i].AttachmentOffsetWS;
                    if (Vector3.Distance(hip, footAnchors[i]) <= reach) low = test;
                    else high = test;
                }
                fraction = Mathf.Min(fraction, low);
            }
            command.DeltaPositionWS *= fraction;
            command.TargetRotationWS = Quaternion.Slerp(start.rotation, endRotation, fraction);
            return command;
        }

        public PlanBuildResult PlanModules(in PlannerInput input, in RootMotionResult rootResult, ModulePlanBuffer destination)
        {
            if (destination == null) return new PlanBuildResult(false, "plan.noBuffer");
            var metrics = input.Metrics.Modules ?? Array.Empty<ModuleMetric>();
            bool snake = morphology == TestMorphology.Snake;
            bool tentacles = morphology == TestMorphology.Jellyfish || morphology == TestMorphology.Octopus;
            int limbCount = snake || tentacles ? 0 : metrics.Length;
            int count = snake ? 1 : tentacles ? SecondaryMotionPlanner.TentacleCount + 1 : limbCount + 1;
            if (count == 0) return new PlanBuildResult(false, "plan.noModules");
            if (footAnchors.Length != limbCount)
            {
                footAnchors = new Vector3[limbCount]; swingOrigins = new Vector3[limbCount]; swingGoals = new Vector3[limbCount];
                footRotations = new Quaternion[limbCount];
                wasSwing = new bool[limbCount]; hasAnchor = new bool[limbCount];
                swingProgress = new float[limbCount];
                stepWindowConsumed = new bool[limbCount];
                swingUsesPrediction = new bool[limbCount];
            }

            var ids = new ModuleId[count];
            var trajectories = new ModuleTrajectory[count];
            Vector3 root = rootResult.ActualRootPoseWS.position;
            Quaternion facing = rootResult.ActualRootPoseWS.rotation;
            double now = input.Tick.Now;
            float motionSpeed = new Vector2(rootResult.ActualVelocityWS.x, rootResult.ActualVelocityWS.z).magnitude;
            float motionWeight = Mathf.Clamp01(motionSpeed / Mathf.Max(0.1f, moveSpeed));
            // Turn intent survives the support constraint: otherwise a blocked body cannot
            // accelerate its stepping. Bound frequency to preserve sampled swing intervals.
            float travelFrequency = MotionFitProfile.Frequency(morphology) * strideFrequency;
            if (coupleSpeedToStride && limbCount > 0)
            {
                // Use planned demand as well as measured motion: planted feet may block
                // translation, but must not suppress the cadence needed to release it.
                float demandedSpeed = Mathf.Max(motionSpeed, driveVelocity.magnitude);
                travelFrequency = Mathf.Max(travelFrequency,
                    demandedSpeed / MotionFitProfile.ReferenceCycleDistance(morphology));
            }
            float targetFrequency = Mathf.Min(4f, travelFrequency * Mathf.Lerp(1f, turnStrideMultiplier, turnDemand));
            effectiveFrequency = Mathf.MoveTowards(effectiveFrequency, targetFrequency, input.Tick.DeltaTime * 8f);
            bool needsSteps = motionSpeed > .005f || driveVelocity.sqrMagnitude > .000025f || turnDemand > .02f;
            if (needsSteps)
                gaitClock = Mathf.Repeat(gaitClock + input.Tick.DeltaTime * effectiveFrequency, 1f);
            int index = 0;
            if (!snake)
            {
                Vector3 body = root + Vector3.up * input.Metrics.BodyHeight;
                float bob = MotionFitProfile.BodyHeightOffset(morphology, gaitClock, motionWeight);
                ids[index] = new ModuleId("Body", ModuleKind.Body);
                trajectories[index] = MakeTrajectory(ids[index], body + Vector3.up * bob, facing, now, ContactPhase.Free, 0);
                index++;
            }
            Vector3 landingVelocity = rootResult.ActualVelocityWS;
            landingVelocity.y = 0f;
            if (coupleSpeedToStride && limbCount > 0)
                landingVelocity = Vector3.Lerp(landingVelocity, driveVelocity, .5f);
            for (int i = 0; i < limbCount; i++)
            {
                var metric = metrics[i];
                Vector3 preferred = root + Vector3.up * input.Metrics.BodyHeight + facing * metric.ContactOffsetWS;
                preferred.y = ProbeHeight(preferred);
                float phase = Mathf.Repeat(gaitClock + MotionFitProfile.PhaseOffset(morphology, i, limbCount), 1f);
                float swingFraction = MotionFitProfile.SwingFraction(morphology);
                bool wantsStep = needsSteps && phase < swingFraction;
                if (!hasAnchor[i])
                {
                    footAnchors[i] = preferred; swingOrigins[i] = preferred; swingGoals[i] = preferred;
                    footRotations[i] = facing; hasAnchor[i] = true;
                }
                if (!wantsStep) stepWindowConsumed[i] = false;
                if (wantsStep && !wasSwing[i] && !stepWindowConsumed[i])
                {
                    stepWindowConsumed[i] = true;
                    wasSwing[i] = true;
                    swingUsesPrediction[i] = coupleSpeedToStride;
                    swingProgress[i] = 0f;
                    swingOrigins[i] = footAnchors[i];
                    float cycleDuration = 1f / Mathf.Max(.1f, effectiveFrequency);
                    float landingLead = cycleDuration * (swingFraction + 0.5f * (1f - swingFraction));
                    Quaternion landingFacing = Quaternion.RotateTowards(facing, desiredFacing, 180f * landingLead);
                    swingGoals[i] = root + Vector3.up * input.Metrics.BodyHeight + landingFacing * metric.ContactOffsetWS +
                        landingVelocity * landingLead;
                    swingGoals[i].y = ProbeHeight(swingGoals[i]);
                    float swingTime = Mathf.Max(.08f, swingFraction / Mathf.Max(.1f, effectiveFrequency));
                    Vector3 landingRoot = swingUsesPrediction[i] ? root + landingVelocity * swingTime : root;
                    swingGoals[i] = ReachableLanding(swingGoals[i], metric, landingRoot, facing, input.Metrics.BodyHeight);
                }
                bool swing = wasSwing[i];
                Vector3 target = footAnchors[i];
                if (swing)
                {
                    float duration = Mathf.Max(.08f, swingFraction / Mathf.Max(.1f, effectiveFrequency));
                    swingProgress[i] = Mathf.Min(1f, swingProgress[i] + input.Tick.DeltaTime / duration);
                    float t = swingProgress[i];
                    if (t < 0.5f || swingUsesPrediction[i])
                    {
                        float cycleDuration = 1f / Mathf.Max(.1f, effectiveFrequency);
                        float horizon = cycleDuration * (swingFraction * (1f - t) + 0.5f * (1f - swingFraction));
                        Quaternion landingFacing = Quaternion.RotateTowards(facing, desiredFacing, 180f * horizon);
                        Vector3 prediction = root + Vector3.up * input.Metrics.BodyHeight + landingFacing * metric.ContactOffsetWS +
                            landingVelocity * horizon;
                        prediction.y = ProbeHeight(prediction);
                        Vector3 landingRoot = swingUsesPrediction[i] ? root + landingVelocity * duration * (1f - t) : root;
                        prediction = ReachableLanding(prediction, metric, landingRoot, facing, input.Metrics.BodyHeight);
                        swingGoals[i] = Vector3.Lerp(swingGoals[i], prediction, 1f - Mathf.Exp(-input.Tick.DeltaTime * 16f));
                    }
                    float blend = t * t * (3f - 2f * t);
                    target = Vector3.Lerp(swingOrigins[i], swingGoals[i], blend);
                    target.y += Mathf.Sin(t * Mathf.PI) * stepHeight;
                    // A predicted body pose is only an intent. Project each swing sample
                    // into today's reach so collisions or cancellation cannot plant an
                    // unreachable anchor. The final projected sample becomes the anchor.
                    target = ReachableLanding(target, metric, root, facing, input.Metrics.BodyHeight);
                    if (t >= 1f)
                    {
                        footAnchors[i] = target;
                        footRotations[i] = facing;
                        target = footAnchors[i];
                        swing = false;
                    }
                }
                wasSwing[i] = swing;
                ids[index] = new ModuleId(metric.ModuleId, ModuleKind.Limb);
                trajectories[index] = MakeTrajectory(ids[index], target, swing ? facing : footRotations[i], now,
                    swing ? ContactPhase.Swing : ContactPhase.Stance, (uint)(i + 1));
                index++;
            }
            if (snake)
            {
                var points = secondary.Snake(new Pose(root + Vector3.up * input.Metrics.BodyHeight, facing),
                    spineCount, rootResult.ActualVelocityWS, input.Tick.DeltaTime);
                ids[index] = new ModuleId("Spine", ModuleKind.Spine);
                trajectories[index] = MakeChainTrajectory(ids[index], points, facing, now, ContactPhase.Free);
            }
            if (tentacles)
            {
                Pose body = trajectories[0].Knots[0].PoseWS;
                var observation = morphology == TestMorphology.Octopus ? GraspableObject.Nearest(body.position, 2.3f) : null;
                var chains = secondary.Tentacles(body, morphology == TestMorphology.Jellyfish, observation, input.Tick.DeltaTime);
                for (int i = 0; i < chains.Length; i++)
                {
                    ids[index] = new ModuleId("Tentacle" + i, ModuleKind.Tentacle);
                    trajectories[index] = MakeChainTrajectory(ids[index], chains[i], facing, now,
                        secondary.HasContact(i) ? ContactPhase.Stance : ContactPhase.Free);
                    index++;
                }
            }
            destination.Publish(new ModulePlanFrame
            {
                ActorId = input.Tick.ActorId, Epoch = input.Tick.Epoch, TickId = input.Tick.TickId,
                PlanSequence = ++sequence, SourceRouteVersion = input.Route != null ? input.Route.RouteVersion : 0,
                GeneratedAt = now, ValidUntil = now + 0.15d, ModuleIds = ids, Trajectories = trajectories
            });
            if (!SnapshotValidation.ValidatePlan(destination.Frame, out var error)) { destination.Clear(); return new PlanBuildResult(false, error); }
            return new PlanBuildResult(true, null);
        }

        private float ProbeHeight(Vector3 point)
        {
            if (ground != null && ground.TryProbe(new GroundQuery { OriginWS = point + Vector3.up * 1.5f, DirectionWS = Vector3.down, Distance = 3f, Radius = 0.02f, LayerMask = (1 << 0) | (1 << 10) }, out var hit))
                return hit.PointWS.y + 0.04f;
            return 0.04f;
        }

        private Vector3 ReachableLanding(Vector3 goal, ModuleMetric metric, Vector3 root, Quaternion facing, float bodyHeight)
        {
            Vector3 hip = root + Vector3.up * (bodyHeight + .04f) + facing * metric.AttachmentOffsetWS;
            float reach = (metric.UpperLength + metric.LowerLength) * .97f;
            float vertical = hip.y - goal.y;
            float radius = Mathf.Sqrt(Mathf.Max(0f, reach * reach - vertical * vertical));
            Vector3 horizontal = goal - hip; horizontal.y = 0f;
            horizontal = Vector3.ClampMagnitude(horizontal, radius);
            return new Vector3(hip.x + horizontal.x, goal.y, hip.z + horizontal.z);
        }

        private static ModuleTrajectory MakeTrajectory(ModuleId id, Vector3 point, Quaternion rotation, double now, ContactPhase phase, uint contact)
        {
            var pose = new Pose(point, rotation);
            var knot = new ModuleTrajectoryKnot { Time = now, PoseWS = pose, VelocityWS = Vector3.zero, PositionWeight = 1f, RotationWeight = 1f, HintPositionWS = point + Vector3.up, HintWeight = 1f, Phase = phase, ContactId = contact };
            var end = knot; end.Time = now + 0.15d;
            return new ModuleTrajectory { Module = id, Knots = new[] { knot, end }, ValidFrom = now, ValidUntil = now + 0.15d, InterpolationMode = InterpolationMode.HermiteSlerp };
        }

        private static ModuleTrajectory MakeChainTrajectory(ModuleId id, Vector3[] points, Quaternion rotation, double now, ContactPhase phase)
        {
            var trajectory = MakeTrajectory(id, points[points.Length - 1], rotation, now, phase, 0);
            trajectory.Knots[0].ChainPointsWS = points;
            trajectory.Knots[1].ChainPointsWS = (Vector3[])points.Clone();
            return trajectory;
        }
    }
}
