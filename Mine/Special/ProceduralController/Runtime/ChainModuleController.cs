using UnityEngine;
using CelToon.ProceduralController.Contracts;

namespace CelToon.ProceduralController.Runtime
{
    /// <summary>L3 writes an independent, world-space chain supplied by L2. It never reads input.</summary>
    public sealed class ChainModuleController : MonoBehaviour, IModuleController
    {
        private string moduleId;
        private ModuleKind kind;
        private Transform[] bones;
        private float restLength;
        public string ModuleId => moduleId;
        public Transform[] Bones => bones;
        public float MaximumLengthError { get; private set; }

        public void Configure(string id, ModuleKind type, Transform[] chain, float length)
        { moduleId = id; kind = type; bones = chain; restLength = length; }
        public void Reset(in ControllerResetContext context) { }

        public ControllerFeedback Apply(in ModuleTargetSample sample, in TickContext tick)
        {
            var feedback = new ControllerFeedback { Module = new ModuleId(moduleId, kind), RequestedTargetWS = sample.PoseWS };
            if (bones == null || bones.Length < 2) { feedback.State = ControllerState.InvalidBinding; return feedback; }
            if (!sample.Valid || sample.ChainPointsWS == null || sample.ChainPointsWS.Length != bones.Length)
            { feedback.State = ControllerState.InvalidInput; return feedback; }
            for (int i = 0; i < bones.Length; i++)
            {
                if (!bones[i]) { feedback.State = ControllerState.InvalidBinding; return feedback; }
                if (!SnapshotValidation.IsFinite(sample.ChainPointsWS[i])) { feedback.State = ControllerState.InvalidInput; return feedback; }
            }
            MaximumLengthError = 0f;
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].position = sample.ChainPointsWS[i];
                Vector3 tangent = i + 1 < bones.Length ? sample.ChainPointsWS[i + 1] - sample.ChainPointsWS[i] :
                    sample.ChainPointsWS[i] - sample.ChainPointsWS[i - 1];
                if (tangent.sqrMagnitude > 0.000001f)
                    bones[i].rotation = Quaternion.LookRotation(tangent.normalized,
                        Mathf.Abs(tangent.normalized.y) > 0.98f ? Vector3.forward : Vector3.up);
                if (i > 0) MaximumLengthError = Mathf.Max(MaximumLengthError,
                    Mathf.Abs(Vector3.Distance(bones[i].position, bones[i - 1].position) - restLength));
            }
            feedback.ActualPoseWS = new Pose(bones[bones.Length - 1].position, bones[bones.Length - 1].rotation);
            feedback.ClampedTargetWS = sample.PoseWS;
            feedback.ReachError = Vector3.Distance(feedback.ActualPoseWS.position, sample.PoseWS.position);
            feedback.State = ControllerState.Solved;
            return feedback;
        }
    }
}
