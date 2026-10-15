using UnityEngine;
using CelToon.ProceduralController.Contracts;

namespace CelToon.ProceduralController.Execution
{
    /// L3 writer for one explicitly bound two-segment module. It has no Update loop or route knowledge.
    public sealed class LimbIKController : MonoBehaviour, IModuleController
    {
        [SerializeField] private RigBinding binding;
        [SerializeField] private string moduleId;
        [SerializeField, Range(0f, 1f)] private float positionWeight = 1f;
        [SerializeField, Range(0f, 1f)] private float rotationWeight = 1f;
        [SerializeField] private float epsilon = 0.00001f;

        private RigBinding.ModuleBinding module;
        private Quaternion baseUpperLocal, baseLowerLocal, baseFootLocal;
        public string ModuleId => moduleId;

        public void Configure(RigBinding rig, string id)
        { binding = rig; moduleId = id; module = null; }

        public void Reset(in ControllerResetContext context)
        {
            Resolve();
            if (module != null) { module.Upper.localRotation = baseUpperLocal; module.Lower.localRotation = baseLowerLocal; module.Foot.localRotation = baseFootLocal; }
        }

        public ControllerFeedback Apply(in ModuleTargetSample sample, in TickContext tick)
        {
            var feedback = new ControllerFeedback { Module = new ModuleId(moduleId, ModuleKind.Limb), State = ControllerState.InvalidBinding, RequestedTargetWS = sample.PoseWS };
            if (!Resolve()) return feedback;
            if (!sample.Valid || !Finite(sample.PoseWS.position) || !Finite(sample.PoseWS.rotation)) { feedback.State = ControllerState.InvalidInput; return feedback; }
            // Restore the reference joint pose each tick so solver input does not drift.
            module.Upper.localRotation = baseUpperLocal;
            module.Lower.localRotation = baseLowerLocal;
            module.Foot.localRotation = baseFootLocal;
            var input = new TwoBoneSolver.Input { RootPosition = module.Upper.position, MidPosition = module.Lower.position, TipPosition = module.Foot.position, TargetPosition = sample.PoseWS.position, HintPosition = sample.HintPositionWS, RootRotation = module.Upper.rotation, MidRotation = module.Lower.rotation, TipRotation = module.Foot.rotation, TargetRotation = sample.PoseWS.rotation, PositionWeight = Mathf.Clamp01(sample.PositionWeight * positionWeight), RotationWeight = Mathf.Clamp01(sample.RotationWeight * rotationWeight), Epsilon = epsilon };
            var solved = TwoBoneSolver.Solve(input);
            if (!solved.Valid) { feedback.State = ControllerState.InvalidInput; feedback.ClampReason = solved.Error; return feedback; }
            module.Upper.rotation = solved.RootRotation;
            module.Lower.rotation = solved.MidRotation;
            module.Foot.rotation = solved.TipRotation;
            feedback.State = solved.Clamped ? ControllerState.Clamped : ControllerState.Solved;
            feedback.ClampedTargetWS = new Pose(solved.ClampedTarget, solved.TipRotation);
            feedback.ActualPoseWS = new Pose(module.Foot.position, module.Foot.rotation);
            feedback.ReachError = Vector3.Distance(module.Foot.position, sample.PoseWS.position);
            feedback.ClampReason = solved.Clamped ? "reach" : null;
            return feedback;
        }

        private bool Resolve()
        {
            if (module != null) return true;
            if (!binding || string.IsNullOrEmpty(moduleId)) return false;
            foreach (var candidate in binding.Modules) if (candidate != null && candidate.ModuleId == moduleId) { module = candidate; baseUpperLocal = candidate.Upper.localRotation; baseLowerLocal = candidate.Lower.localRotation; baseFootLocal = candidate.Foot.localRotation; return true; }
            return false;
        }
        private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        private static bool Finite(Quaternion q) => !float.IsNaN(q.x) && !float.IsInfinity(q.x) && !float.IsNaN(q.y) && !float.IsInfinity(q.y) && !float.IsNaN(q.z) && !float.IsInfinity(q.z) && !float.IsNaN(q.w) && !float.IsInfinity(q.w);
    }
}
