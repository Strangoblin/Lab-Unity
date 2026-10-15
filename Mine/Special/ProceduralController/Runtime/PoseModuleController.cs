using UnityEngine;
using CelToon.ProceduralController.Contracts;

namespace CelToon.ProceduralController.Runtime
{
    /// <summary>L3 writer for a body or snake spine segment; it consumes only module samples.</summary>
    public sealed class PoseModuleController : MonoBehaviour, IModuleController
    {
        [SerializeField] private string moduleId;
        [SerializeField] private ModuleKind kind;
        [SerializeField] private Transform bone;
        public string ModuleId => moduleId;

        public void Configure(string id, ModuleKind moduleKind, Transform target)
        { moduleId = id; kind = moduleKind; bone = target; }

        public void Reset(in ControllerResetContext context) { }

        public ControllerFeedback Apply(in ModuleTargetSample sample, in TickContext tick)
        {
            var feedback = new ControllerFeedback { Module = new ModuleId(moduleId, kind), RequestedTargetWS = sample.PoseWS };
            if (!bone) { feedback.State = ControllerState.InvalidBinding; return feedback; }
            if (!sample.Valid || !SnapshotValidation.IsValid(sample.PoseWS)) { feedback.State = ControllerState.InvalidInput; return feedback; }
            bone.SetPositionAndRotation(sample.PoseWS.position, sample.PoseWS.rotation);
            feedback.State = ControllerState.Solved;
            feedback.ClampedTargetWS = sample.PoseWS;
            feedback.ActualPoseWS = new Pose(bone.position, bone.rotation);
            feedback.ReachError = 0f;
            return feedback;
        }
    }
}
