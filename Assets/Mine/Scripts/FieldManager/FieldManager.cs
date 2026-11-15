using UnityEngine;
using System.Collections.Generic;

namespace Mine.Fields
{
    /// <summary>Schedules shared world fields before simulations; providers retain resource ownership.</summary>
    [ExecuteAlways, DefaultExecutionOrder(-1000)]
    public sealed class FieldManager : MonoBehaviour
    {
        private static readonly List<FieldProvider> Providers = new List<FieldProvider>();
        public static FieldManager Instance { get; private set; }
        public static bool HasActiveManager => Instance != null && Instance.isActiveAndEnabled;
        public static float Clock => Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
        private int _lastFrame = -1;
        private float _lastTime;

        // ════════════════════════════════════════════════════════════
        //  Lifecycle and scheduling — sorted by each provider's FieldOrder
        // ════════════════════════════════════════════════════════════
        private void OnEnable()
        {
            if (HasActiveManager && Instance != this)
            {
                Debug.LogError("Only one active FieldManager may publish scene fields.", this);
                enabled = false;
                return;
            }
            Instance = this;
            _lastFrame = -1;
            _lastTime = Clock;
            UpdateFields();
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void Update() => UpdateFields();

        public void UpdateFields()
        {
            if (Application.isPlaying && _lastFrame == Time.frameCount) return;
            float time = Clock;
            float deltaTime = Mathf.Max(0f, time - _lastTime);
            _lastFrame = Time.frameCount;
            _lastTime = time;
            for (int i = 0; i < Providers.Count; i++)
            {
                var provider = Providers[i];
                if (provider == null) { Providers.RemoveAt(i--); continue; }
                if (provider.isActiveAndEnabled && provider.IsReady) provider.Advance(time, deltaTime);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Registration and explicit Compute bindings
        // ════════════════════════════════════════════════════════════
        internal static bool Register(FieldProvider provider)
        {
            if (string.IsNullOrEmpty(provider.FieldKey))
            {
                Debug.LogError("Field provider must declare a non-empty FieldKey.", provider);
                return false;
            }
            for (int i = Providers.Count - 1; i >= 0; i--)
            {
                var other = Providers[i];
                if (other == null) { Providers.RemoveAt(i); continue; }
                if (other == provider) return true;
                if (string.Equals(other.FieldKey, provider.FieldKey, System.StringComparison.Ordinal))
                {
                    Debug.LogError("Duplicate field producer for '" + provider.FieldKey + "'; disable the existing producer first.", provider);
                    return false;
                }
            }

            // Stable insertion: only providers with a strictly larger order move down.
            int index = Providers.Count;
            for (int i = 0; i < Providers.Count; i++)
            {
                var other = Providers[i];
                if (other != null && other.FieldOrder > provider.FieldOrder) { index = i; break; }
            }
            Providers.Insert(index, provider);
            return true;
        }

        internal static void Unregister(FieldProvider provider)
        {
            if (!Providers.Remove(provider)) return;
            provider.ResetGlobals();
        }

        public static FieldProvider GetProvider(string fieldKey)
        {
            if (string.IsNullOrEmpty(fieldKey)) return null;
            for (int i = Providers.Count - 1; i >= 0; i--)
            {
                var provider = Providers[i];
                if (provider == null) { Providers.RemoveAt(i); continue; }
                if (string.Equals(provider.FieldKey, fieldKey, System.StringComparison.Ordinal)
                    && provider.isActiveAndEnabled && provider.IsReady) return provider;
            }
            return null;
        }

        /// <summary>
        /// Binds the channel's snapshot to a Compute kernel. When no provider is active the
        /// channel's own neutral state is bound instead, so the kernel never keeps released textures.
        /// </summary>
        public static bool BindCompute(IFieldBindings bindings, ComputeShader shader, int kernel)
        {
            if (bindings == null) return false;
            if (HasActiveManager && Application.isPlaying) Instance.UpdateFields();
            var provider = GetProvider(bindings.FieldKey);
            if (provider == null)
            {
                bindings.BindNeutral(shader, kernel);
                return false;
            }
            provider.BindCompute(shader, kernel);
            return true;
        }
    }
}
