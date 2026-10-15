using UnityEngine;
using CelToon.ProceduralController.Contracts;

namespace CelToon.ProceduralController.Runtime
{
    /// <summary>L3 is the sole root transform writer.</summary>
    public sealed class RootMotionController : MonoBehaviour, IRootMotionExecutor
    {
        [SerializeField] private CharacterController characterController;
        private float verticalVelocity;
        private Transform motionTransform;
        private bool floating;
        public Transform MotionTransform => motionTransform ? motionTransform : transform;

        public void Configure(CharacterController controller, Transform drivenPart = null, bool isFloating = false)
        { characterController = controller; motionTransform = drivenPart ? drivenPart : controller ? controller.transform : transform; floating = isFloating; }

        public RootMotionResult Apply(in RootMotionCommand command, in TickContext tick)
        {
            Transform driver = MotionTransform;
            Vector3 before = driver.position;
            CollisionFlags flags = CollisionFlags.None;
            if (characterController)
            {
                verticalVelocity = floating ? 0f : characterController.isGrounded ? -0.5f : Mathf.Max(verticalVelocity - 9.81f * tick.DeltaTime, -12f);
                Vector3 motion = command.DeltaPositionWS + Vector3.up * verticalVelocity * tick.DeltaTime;
                flags = characterController.Move(motion);
                if ((flags & CollisionFlags.Below) != 0) verticalVelocity = -0.5f;
            }
            else
            {
                driver.position += command.DeltaPositionWS;
            }
            if (SnapshotValidation.IsValid(command.TargetRotationWS))
                driver.rotation = Quaternion.RotateTowards(driver.rotation, command.TargetRotationWS, command.MaxAngularSpeed * tick.DeltaTime);
            Vector3 applied = driver.position - before;
            return new RootMotionResult
            {
                ActorId = tick.ActorId, Epoch = tick.Epoch, TickId = tick.TickId,
                RequestedDeltaWS = command.DeltaPositionWS, AppliedDeltaWS = applied,
                ActualRootPoseWS = new Pose(driver.position, driver.rotation),
                ActualVelocityWS = applied / Mathf.Max(tick.DeltaTime, 0.0001f),
                CollisionFlags = flags, Grounded = characterController && characterController.isGrounded
            };
        }
    }
}
