using CelToon.PathGenerator.Contracts;
using System;
using System.Collections.Generic;
using UnityEngine;
using CelToon.ProceduralController.Contracts;
using CelToon.ProceduralController.Execution;

namespace CelToon.ProceduralController.Runtime
{
    /// <summary>Per-actor scheduler. The three business layers communicate only through contract values.</summary>
    public sealed class LocomotionPipelineHost : MonoBehaviour
    {
        [SerializeField] private bool autoUpdate = true;
        private IRouteProvider routeProvider;
        private bool providerMode;
        private RouteSnapshot activeRoute;
        private RouteSnapshot observedProviderRoute;
        private uint observedProviderVersion;
        private uint localRouteVersion;
        private ProceduralAnimationManager planner;
        private RootMotionController rootController;
        private RigBinding binding;
        private IModuleController[] controllers = Array.Empty<IModuleController>();
        private readonly ModulePlanBuffer buffer = new ModulePlanBuffer();
        private uint epoch = 1;
        private uint requestId;
        private ulong tickId;
        private RigMetrics metrics;
        private ControllerFeedback[] feedback = Array.Empty<ControllerFeedback>();
        private Vector3 previousPosition;
        private Vector3 lastActualVelocity;
        private bool initialized;

        public TestMorphology Morphology { get; private set; }
        public RouteSnapshot LastRoute { get; private set; }
        public ModulePlanFrame LastPlan => buffer.Frame;
        public ControllerFeedback[] LastFeedback => feedback;
        public string LastError { get; private set; }
        public int SuccessfulFrames { get; private set; }
        public int FailedFrames { get; private set; }
        public float LastMaxReachError { get; private set; }
        public bool IsInitialized => initialized;
        public int ActorId => GetInstanceID();
        public uint Epoch => epoch;
        public bool AutoUpdate { get => autoUpdate; set => autoUpdate = value; }
        public Transform MotionAnchor => rootController ? rootController.MotionTransform : transform;
        public Transform Body => binding ? binding.Body : null;
        public int GraspingTentacles => planner ? planner.GraspingTentacles : 0;
        public float Propulsion => planner ? planner.Propulsion : 0f;
        public RootMotionResult LastMotionResult { get; private set; }

        public bool Configure(TestMorphology shape, ProceduralAnimationManager animationManager,
            RootMotionController motionController, RigBinding rig, IModuleController[] moduleControllers, int snakeSegments,
            IGroundProbe groundProbe = null)
        {
            if (initialized) { Stop(); epoch++; }
            initialized = false;
            Morphology = shape; planner = animationManager;
            rootController = motionController; binding = rig;
            controllers = moduleControllers ?? Array.Empty<IModuleController>();
            if (!planner || !rootController || !binding || !binding.Body)
            { LastError = "host.missingLayer"; return false; }
            bool soft = shape == TestMorphology.Snake || shape == TestMorphology.Jellyfish || shape == TestMorphology.Octopus;
            if (!soft && !binding.TryBuildMetrics(out metrics, out var bindingError))
            { LastError = bindingError; return false; }
            if (soft) metrics = new RigMetrics { Modules = Array.Empty<ModuleMetric>() };
            metrics.BodyHeight = binding.Body.position.y - MotionAnchor.position.y;
            var probe = groundProbe ?? GetComponent<CelToon.ProceduralController.Environment.PhysicsGroundProbe>();
            planner.Configure(shape, probe, snakeSegments);
            foreach (var controller in controllers) controller.Reset(new ControllerResetContext(GetInstanceID(), epoch));
            previousPosition = MotionAnchor.position;
            lastActualVelocity = Vector3.zero;
            routeProvider = null; providerMode = false; observedProviderRoute = null;
            requestId = 0; tickId = 0; localRouteVersion = 0;
            activeRoute = EmptyRoute(RouteStatus.None);
            feedback = Array.Empty<ControllerFeedback>();
            SuccessfulFrames = 0; FailedFrames = 0;
            initialized = true; LastError = null;
            return true;
        }

        public void UseRouteProvider(IRouteProvider provider)
        {
            if (ProviderAvailable()) routeProvider.Cancel(ActorId, epoch);
            routeProvider = provider; providerMode = provider != null;
            observedProviderRoute = null;
            activeRoute = EmptyRoute(RouteStatus.None);
        }

        /// <summary>Forwards a destination to an optional provider; never performs pathfinding.</summary>
        public bool SetDestination(Vector3 destination)
        {
            if (!initialized) return false;
            if (!ProviderAvailable()) { RejectRoute("host.noRouteProvider"); return false; }
            providerMode = true; observedProviderRoute = null;
            routeProvider.Submit(new RouteRequest
            {
                ActorId = ActorId, Epoch = epoch, RequestId = ++requestId,
                StartWS = MotionAnchor.position, DestinationWS = destination,
                AreaMask = 1, StopDistance = planner.StoppingDistance, RequestedAt = Time.timeAsDouble
            });
            return true;
        }

        /// <summary>Consumes caller-supplied world points without any path generator.</summary>
        public bool SetPath(IReadOnlyList<Vector3> points, double validUntil = double.MaxValue)
        {
            if (!initialized) return false;
            if (points == null) { Stop(); RejectRoute("route.null"); return false; }
            if (points.Count == 0) { Stop(); return true; }
            int count = points.Count == 1 ? 2 : points.Count;
            var waypoints = new RouteWaypoint[count];
            float distance = 0f;
            Vector3 previous = points.Count == 1 ? MotionAnchor.position : points[0];
            for (int i = 0; i < count; i++)
            {
                Vector3 point = points.Count == 1 ? i == 0 ? MotionAnchor.position : points[0] : points[i];
                if (i > 0) distance += Vector3.Distance(previous, point);
                waypoints[i] = new RouteWaypoint(point, distance); previous = point;
            }
            return SubmitRoute(new RouteSnapshot(waypoints)
            {
                ActorId = ActorId, Epoch = epoch, RequestId = ++requestId, Status = RouteStatus.Complete,
                RequestedDestinationWS = points[points.Count - 1], ResolvedDestinationWS = points[points.Count - 1],
                CreatedAt = Time.timeAsDouble, ValidUntil = validUntil
            });
        }

        /// <summary>Copies and validates a route at the actor boundary. Caller data is never retained as writable input.</summary>
        public bool SubmitRoute(RouteSnapshot route)
        {
            if (!initialized) return false;
            if (ProviderAvailable()) routeProvider.Cancel(ActorId, epoch);
            providerMode = false; observedProviderRoute = null;
            return AcceptRoute(route);
        }

        public void Stop()
        {
            if (ProviderAvailable()) routeProvider.Cancel(ActorId, epoch);
            providerMode = false;
            activeRoute = EmptyRoute(RouteStatus.Cancelled);
        }

        private void Update()
        {
            if (autoUpdate) Tick(Mathf.Max(Time.deltaTime, .0001f), Time.timeAsDouble);
        }

        /// <summary>External schedulers may disable AutoUpdate and advance this individual explicitly.</summary>
        public void Tick(float deltaTime, double now)
        {
            if (!initialized) return;
            if (!PathValidation.IsFinite(deltaTime) || deltaTime <= 0f || !PathValidation.IsFinite(now))
            { LastError = "host.invalidTick"; return; }
            var tick = new TickContext { ActorId = ActorId, Epoch = epoch, TickId = ++tickId, Now = now, DeltaTime = deltaTime };
            if (providerMode) ReadProviderRoute();
            var route = activeRoute;
            LastRoute = route;
            string navigationError = null;
            if (route.Status == RouteStatus.Invalid || (route.Status == RouteStatus.Complete && route.ValidUntil < tick.Now))
            {
                navigationError = route.Status == RouteStatus.Invalid ? route.FailureCode ?? "route.invalid" : "host.routeExpired";
                route = new RouteSnapshot(Array.Empty<RouteWaypoint>())
                { ActorId = tick.ActorId, Epoch = epoch, Status = RouteStatus.None, RouteVersion = route.RouteVersion };
            }
            var input = new PlannerInput
            {
                Tick = tick, Route = route, Metrics = metrics, Feedback = feedback,
                Character = new CharacterState
                {
                    RootPoseWS = new Pose(MotionAnchor.position, MotionAnchor.rotation),
                    BodyPoseWS = new Pose(binding.Body.position, binding.Body.rotation),
                    ActualVelocityWS = lastActualVelocity + (MotionAnchor.position - previousPosition) / deltaTime
                }
            };
            var command = planner.PlanRoot(input);
            var rootResult = rootController.Apply(command, tick);
            LastMotionResult = rootResult;
            lastActualVelocity = rootResult.ActualVelocityWS;
            previousPosition = MotionAnchor.position;
            buffer.BeginWrite();
            var result = planner.PlanModules(input, rootResult, buffer);
            if (!result.Success || !buffer.IsPublished)
            { LastError = result.FailureCode ?? "host.emptyPlan"; FailedFrames++; return; }
            var frame = buffer.Frame;
            if (!SnapshotValidation.ValidatePlan(frame, out var error) || frame.ActorId != tick.ActorId ||
                frame.Epoch != epoch || frame.TickId != tick.TickId || frame.ValidUntil < tick.Now)
            { LastError = error ?? "host.stalePlan"; FailedFrames++; return; }
            if (frame.ModuleIds.Length != controllers.Length)
            { LastError = "host.moduleCount"; FailedFrames++; return; }
            if (feedback.Length != controllers.Length) feedback = new ControllerFeedback[controllers.Length];
            float maxError = 0f;
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i].ModuleId != frame.ModuleIds[i].Value)
                { LastError = "host.moduleOrder:" + i; FailedFrames++; return; }
                ModuleTargetSample sample = TrajectorySampling.Sample(frame.Trajectories[i], tick.Now);
                feedback[i] = controllers[i].Apply(sample, tick);
                maxError = Mathf.Max(maxError, feedback[i].ReachError);
                if (feedback[i].State == ControllerState.InvalidBinding || feedback[i].State == ControllerState.InvalidInput)
                { LastError = "host.controller:" + controllers[i].ModuleId + ":" + feedback[i].State; FailedFrames++; return; }
            }
            LastMaxReachError = maxError;
            LastError = navigationError;
            SuccessfulFrames++;
        }

        private bool ProviderAvailable() => routeProvider != null &&
            (!(routeProvider is UnityEngine.Object component) || component);

        private RouteSnapshot EmptyRoute(RouteStatus status, string error = null) =>
            new RouteSnapshot(Array.Empty<RouteWaypoint>())
            { ActorId = ActorId, Epoch = epoch, RouteVersion = ++localRouteVersion, Status = status, FailureCode = error };

        private void RejectRoute(string error)
        {
            activeRoute = EmptyRoute(RouteStatus.Invalid, error);
            LastError = error;
        }

        private bool AcceptRoute(RouteSnapshot source)
        {
            if (source != null && (source.ActorId != ActorId || source.Epoch != epoch))
            { RejectRoute("host.routeIdentity"); return false; }
            if (!PathValidation.ValidateRoute(source, out var error)) { RejectRoute(error); return false; }
            activeRoute = new RouteSnapshot(source.Waypoints)
            {
                ActorId = ActorId, Epoch = epoch, RequestId = source.RequestId, RouteVersion = ++localRouteVersion,
                Status = source.Status, RequestedDestinationWS = source.RequestedDestinationWS,
                ResolvedDestinationWS = source.ResolvedDestinationWS, CreatedAt = source.CreatedAt,
                ValidUntil = source.ValidUntil, FailureCode = source.FailureCode
            };
            return true;
        }

        private void ReadProviderRoute()
        {
            if (!ProviderAvailable())
            { if (activeRoute.FailureCode != "host.routeProviderUnavailable") RejectRoute("host.routeProviderUnavailable"); return; }
            if (!routeProvider.TryGetLatest(ActorId, epoch, out var source) || source == null)
            {
                if (activeRoute.Status != RouteStatus.None) activeRoute = EmptyRoute(RouteStatus.None);
                observedProviderRoute = null; return;
            }
            if (!ReferenceEquals(source, observedProviderRoute) || source.RouteVersion != observedProviderVersion)
            {
                observedProviderRoute = source; observedProviderVersion = source.RouteVersion;
                AcceptRoute(source);
            }
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;
            if (LastRoute != null && LastRoute.Waypoints != null)
            {
                Gizmos.color = Color.green;
                for (int i = 1; i < LastRoute.Waypoints.Length; i++)
                    Gizmos.DrawLine(LastRoute.Waypoints[i - 1].PositionWS + Vector3.up * 0.08f,
                        LastRoute.Waypoints[i].PositionWS + Vector3.up * 0.08f);
            }
            if (LastPlan == null || LastPlan.Trajectories == null) return;
            for (int i = 0; i < LastPlan.Trajectories.Length; i++)
            {
                var trajectory = LastPlan.Trajectories[i];
                if (trajectory?.Knots == null || trajectory.Knots.Length == 0) continue;
                Vector3 requested = trajectory.Knots[0].PoseWS.position;
                Gizmos.color = trajectory.Module.Kind == ModuleKind.Limb ? Color.yellow : Color.magenta;
                Gizmos.DrawWireSphere(requested, 0.1f);
                if (i >= feedback.Length) continue;
                Gizmos.color = Color.red;
                Gizmos.DrawLine(requested, feedback[i].ActualPoseWS.position);
            }
        }
    }
}
