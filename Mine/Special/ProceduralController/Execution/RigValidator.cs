using System.Collections.Generic;
using UnityEngine;
using CelToon.ProceduralController.Contracts;

namespace CelToon.ProceduralController.Execution
{
    public static class RigValidator
    {
        public static bool Validate(RigBinding binding, out string error)
        {
            error = null;
            if (!binding) { error = "binding.null"; return false; }
            if (!binding.Body) { error = "binding.body"; return false; }
            if (binding.Modules == null || binding.Modules.Length == 0) { error = "binding.modules.empty"; return false; }
            var ids = new HashSet<string>();
            foreach (var module in binding.Modules)
            {
                if (module == null || string.IsNullOrWhiteSpace(module.ModuleId)) { error = "module.id"; return false; }
                if (!ids.Add(module.ModuleId)) { error = "module.duplicate:" + module.ModuleId; return false; }
                if (!module.Upper || !module.Lower || !module.Foot) { error = "module.reference:" + module.ModuleId; return false; }
                if (module.ActorId != 0 && module.ActorId != binding.ActorId) { error = "module.crossActor:" + module.ModuleId; return false; }
                if (module.Upper == module.Lower || module.Lower == module.Foot || !IsDescendant(module.Lower, module.Upper) || !IsDescendant(module.Foot, module.Lower)) { error = "module.ancestry:" + module.ModuleId; return false; }
                if (!FinitePositive(Vector3.Distance(module.Upper.position, module.Lower.position)) || !FinitePositive(Vector3.Distance(module.Lower.position, module.Foot.position))) { error = "module.length:" + module.ModuleId; return false; }
                if (!SameActor(binding.transform, module.Upper) || !SameActor(binding.transform, module.Lower) || !SameActor(binding.transform, module.Foot)) { error = "module.crossActor:" + module.ModuleId; return false; }
                if (!ScaleValid(module.Upper.lossyScale) || !ScaleValid(module.Lower.lossyScale) || !ScaleValid(module.Foot.lossyScale)) { error = "module.scale:" + module.ModuleId; return false; }
            }
            return true;
        }

        private static bool IsDescendant(Transform child, Transform ancestor)
        { for (var t = child.parent; t; t = t.parent) if (t == ancestor) return true; return false; }
        private static bool SameActor(Transform root, Transform value)
        { for (var t = value; t; t = t.parent) if (t == root) return true; return false; }
        private static bool FinitePositive(float value) => SnapshotValidation.IsFinite(value) && value > 0.0001f;
        private static bool ScaleValid(Vector3 scale) => SnapshotValidation.IsFinite(scale) && scale.x > 0f && scale.y > 0f && scale.z > 0f && Mathf.Abs(scale.x - scale.y) < 0.001f && Mathf.Abs(scale.y - scale.z) < 0.001f;
    }
}
