using UnityEngine;
using CelToon.ProceduralController.Contracts;

namespace CelToon.ProceduralController.Environment
{
    public sealed class CharacterStateReader : MonoBehaviour
    {
        [SerializeField] private Transform root;
        [SerializeField] private Transform body;
        private Vector3 previousPosition;
        private bool initialized;
        public CharacterState Read(float deltaTime)
        {
            if (!root) root = transform;
            if (!body) body = root;
            Vector3 velocity = Vector3.zero;
            if (initialized && deltaTime > 0f) velocity = (root.position - previousPosition) / deltaTime;
            previousPosition = root.position; initialized = true;
            return new CharacterState { RootPoseWS = new Pose(root.position, root.rotation), ActualVelocityWS = velocity, Grounded = Physics.Raycast(root.position + Vector3.up * 0.05f, Vector3.down, 0.15f), BodyPoseWS = new Pose(body.position, body.rotation) };
        }
        public void ResetReader() { initialized = false; }
    }
}
