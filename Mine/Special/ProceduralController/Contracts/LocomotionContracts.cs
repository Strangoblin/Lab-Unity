using CelToon.PathGenerator.Contracts;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CelToon.ProceduralController.Contracts
{
    public enum ModuleKind { Body, Limb, Spine, Tentacle }
    public enum ContactPhase { Free, Stance, Swing, Landing }
    public enum InterpolationMode { HermiteSlerp }
    public enum ControllerState { Solved, Clamped, InvalidInput, InvalidBinding, Stale, Disabled }

    [Serializable]
    public struct TickContext
    { public int ActorId; public uint Epoch; public ulong TickId; public double Now; public float DeltaTime; }

    [Serializable]
    public struct CharacterState
    {
        public Pose RootPoseWS; public Vector3 ActualVelocityWS; public bool Grounded;
        public Pose BodyPoseWS;
    }

    [Serializable]
    public struct RigMetrics { public float BodyHeight; public float SupportMargin; public ModuleMetric[] Modules; }

    [Serializable]
    public struct ModuleMetric
    { public string ModuleId; public float UpperLength; public float LowerLength; public Vector3 AttachmentOffsetWS; public Vector3 ContactOffsetWS; }

    [Serializable]
    public struct GroundQuery
    { public Vector3 OriginWS; public Vector3 DirectionWS; public float Distance; public float Radius; public int LayerMask; }

    [Serializable]
    public struct GroundHit
    { public bool Found; public Vector3 PointWS; public Vector3 NormalWS; public float Distance; public int SurfaceId; public bool IsStatic; }

    [Serializable]
    public struct ModuleId
    {
        public string Value; public ModuleKind Kind;
        public ModuleId(string value, ModuleKind kind) { Value = value; Kind = kind; }
        public override string ToString() => Value ?? string.Empty;
    }

    [Serializable]
    public struct ModuleTrajectoryKnot
    {
        public Vector3[] ChainPointsWS;
        public double Time; public Pose PoseWS; public Vector3 VelocityWS;
        public float PositionWeight; public float RotationWeight; public Vector3 HintPositionWS; public float HintWeight;
        public Vector3 ContactNormalWS; public ContactPhase Phase; public uint ContactId;
    }

    [Serializable]
    public sealed class ModuleTrajectory
    {
        public ModuleId Module; public ModuleTrajectoryKnot[] Knots;
        public double ValidFrom; public double ValidUntil; public InterpolationMode InterpolationMode;
    }

    [Serializable]
    public sealed class ModulePlanFrame
    {
        public int ActorId; public uint Epoch; public ulong TickId; public uint PlanSequence; public uint SourceRouteVersion;
        public double GeneratedAt; public double ValidUntil; public ModuleId[] ModuleIds; public ModuleTrajectory[] Trajectories;
    }

    [Serializable]
    public struct ModuleTargetSample
    { public Vector3[] ChainPointsWS; public double SampleTime; public Pose PoseWS; public float PositionWeight; public float RotationWeight; public Vector3 HintPositionWS; public float HintWeight; public ContactPhase Phase; public uint ContactId; public bool Valid; }

    [Serializable]
    public struct RootMotionCommand
    { public int ActorId; public uint Epoch; public ulong TickId; public Vector3 DeltaPositionWS; public Quaternion TargetRotationWS; public float MaxAngularSpeed; public double ValidUntil; }

    [Serializable]
    public struct RootMotionResult
    { public int ActorId; public uint Epoch; public ulong TickId; public Vector3 RequestedDeltaWS; public Vector3 AppliedDeltaWS; public Pose ActualRootPoseWS; public Vector3 ActualVelocityWS; public CollisionFlags CollisionFlags; public bool Grounded; }

    [Serializable]
    public struct ControllerFeedback
    { public ModuleId Module; public ControllerState State; public Pose RequestedTargetWS; public Pose ClampedTargetWS; public Pose ActualPoseWS; public float ReachError; public string ClampReason; public float SampleAge; }

    public readonly struct PlannerResetContext { public readonly int ActorId; public readonly uint Epoch; public PlannerResetContext(int actorId, uint epoch) { ActorId = actorId; Epoch = epoch; } }
    public readonly struct ControllerResetContext { public readonly int ActorId; public readonly uint Epoch; public ControllerResetContext(int actorId, uint epoch) { ActorId = actorId; Epoch = epoch; } }
    public struct PlannerInput { public TickContext Tick; public RouteSnapshot Route; public CharacterState Character; public RigMetrics Metrics; public ControllerFeedback[] Feedback; }
    public struct PlanBuildResult { public bool Success; public string FailureCode; public PlanBuildResult(bool success, string failureCode) { Success = success; FailureCode = failureCode; } }

    public interface IGroundProbe { bool TryProbe(in GroundQuery query, out GroundHit hit); }
    public interface IProceduralPlanner { void Reset(in PlannerResetContext context); RootMotionCommand PlanRoot(in PlannerInput input); PlanBuildResult PlanModules(in PlannerInput input, in RootMotionResult rootResult, ModulePlanBuffer destination); }
    public interface IRootMotionExecutor { RootMotionResult Apply(in RootMotionCommand command, in TickContext tick); }
    public interface IModuleController { string ModuleId { get; } void Reset(in ControllerResetContext context); ControllerFeedback Apply(in ModuleTargetSample sample, in TickContext tick); }

    public sealed class ModulePlanBuffer
    {
        private ModulePlanFrame frame;
        public bool IsPublished { get; private set; }
        public ModulePlanFrame Frame => frame;
        public void BeginWrite() { frame = new ModulePlanFrame { ModuleIds = Array.Empty<ModuleId>(), Trajectories = Array.Empty<ModuleTrajectory>() }; IsPublished = false; }
        public void Publish(ModulePlanFrame value) { if (value == null) throw new ArgumentNullException(nameof(value)); frame = value; IsPublished = true; }
        public void Clear() { frame = null; IsPublished = false; }
    }
}
