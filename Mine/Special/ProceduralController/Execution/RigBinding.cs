using System;
using UnityEngine;
using CelToon.ProceduralController.Contracts;

namespace CelToon.ProceduralController.Execution
{
    public sealed class RigBinding : MonoBehaviour
    {
        [Serializable]
        public sealed class ModuleBinding
        {
            public string ModuleId;
            public ModuleKind Kind = ModuleKind.Limb;
            public bool Required = true;
            public Transform Upper;
            public Transform Lower;
            public Transform Foot;
            [HideInInspector] public int ActorId;
        }

        [SerializeField] private int actorId;
        [SerializeField] private RigDefinition definition;
        [SerializeField] private Transform body;
        [SerializeField] private ModuleBinding[] modules = Array.Empty<ModuleBinding>();
        [SerializeField, Min(0.0001f)] private float scaleTolerance = 0.05f;

        public int ActorId => actorId;
        public RigDefinition Definition => definition;
        public Transform Body => body;
        public ModuleBinding[] Modules => modules ?? Array.Empty<ModuleBinding>();
        public float ScaleTolerance => scaleTolerance;

        public void Configure(int id, Transform bodyTransform, ModuleBinding[] limbBindings)
        {
            actorId = id;
            body = bodyTransform;
            modules = limbBindings ?? Array.Empty<ModuleBinding>();
            foreach (var module in modules) if (module != null) module.ActorId = id;
        }

        public bool TryBuildMetrics(out RigMetrics metrics, out string error)
        {
            if (!RigValidator.Validate(this, out error)) { metrics = default; return false; }
            var values = new ModuleMetric[modules.Length];
            for (var i = 0; i < modules.Length; i++)
            {
                var m = modules[i];
                values[i] = new ModuleMetric { ModuleId = m.ModuleId, UpperLength = Vector3.Distance(m.Upper.position, m.Lower.position), LowerLength = Vector3.Distance(m.Lower.position, m.Foot.position), AttachmentOffsetWS = m.Upper.position - body.position, ContactOffsetWS = m.Foot.position - body.position };
            }
            metrics = new RigMetrics { BodyHeight = body ? body.position.y - transform.position.y : 0f, Modules = values };
            return true;
        }
    }
}
