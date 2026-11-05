using UnityEngine;

namespace Mine.Fields
{
    /// <summary>
    /// Stable identity and neutral shader state for one field channel.
    /// A bindings instance is the single owner of the channel's global property IDs;
    /// providers publish through it and consumers fall back through it when absent.
    /// </summary>
    public interface IFieldBindings
    {
        string FieldKey { get; }

        /// <summary>Explicit per-kernel fallback so a kernel never keeps a released texture.</summary>
        void BindNeutral(ComputeShader shader, int kernel);

        /// <summary>Global fallback shared by provider reset and absent-provider paths.</summary>
        void BindNeutralGlobals();
    }
}
