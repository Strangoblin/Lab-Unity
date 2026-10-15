using System;
using UnityEngine;
using CelToon.ProceduralController.Contracts;

namespace CelToon.ProceduralController.Execution
{
    [CreateAssetMenu(menuName = "CelToon/Locomotion/Rig Definition")]
    public sealed class RigDefinition : ScriptableObject
    {
        [Serializable]
        public struct ModuleDefinition
        {
            public string ModuleId;
            public ModuleKind Kind;
            public bool Required;
            [Min(0.0001f)] public float UpperLength;
            [Min(0.0001f)] public float LowerLength;
            public Vector3 FootOffset;
        }

        [SerializeField] private string rigId = "Rig";
        [SerializeField] private float unitScale = 1f;
        [SerializeField] private float scaleTolerance = 0.05f;
        [SerializeField] private ModuleDefinition[] modules = Array.Empty<ModuleDefinition>();

        public string RigId => rigId;
        public float UnitScale => unitScale;
        public float ScaleTolerance => scaleTolerance;
        public ModuleDefinition[] Modules => modules ?? Array.Empty<ModuleDefinition>();
    }
}
