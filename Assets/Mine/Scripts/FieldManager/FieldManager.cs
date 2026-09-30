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
        //  Lifecycle and scheduling — wind precedes waves, then consumers
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
            for (int kind = 0; kind <= (int)FieldKind.Acceleration; kind++)
            {
                var provider = GetProvider((FieldKind)kind);
                if (provider != null) provider.Advance(time, deltaTime);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Registration and explicit Compute bindings
        // ════════════════════════════════════════════════════════════
        internal static bool Register(FieldProvider provider)
        {
            var existing = GetProvider(provider.Kind);
            if (existing != null && existing != provider)
            {
                Debug.LogError("Duplicate field producer for " + provider.Kind + "; disable the existing producer first.", provider);
                return false;
            }
            if (!Providers.Contains(provider)) Providers.Add(provider);
            return true;
        }

        internal static void Unregister(FieldProvider provider)
        {
            if (!Providers.Remove(provider)) return;
            provider.ResetGlobals();
        }

        public static FieldProvider GetProvider(FieldKind kind)
        {
            for (int i = Providers.Count - 1; i >= 0; i--)
            {
                var provider = Providers[i];
                if (provider == null) { Providers.RemoveAt(i); continue; }
                if (provider.Kind == kind && provider.isActiveAndEnabled && provider.IsReady) return provider;
            }
            return null;
        }

        public static bool BindCompute(FieldKind kind, ComputeShader shader, int kernel)
        {
            if (HasActiveManager && Application.isPlaying) Instance.UpdateFields();
            var provider = GetProvider(kind);
            if (provider == null)
            {
                if (kind == FieldKind.Wind) WindFieldProvider.BindNeutral(shader, kernel);
                else if (kind == FieldKind.Wave) WaveFieldBindings.BindNeutral(shader, kernel);
                return false;
            }
            provider.BindCompute(shader, kernel);
            return true;
        }

        public static void BindWindCompute(ComputeShader shader, int kernel)
        {
            BindCompute(FieldKind.Wind, shader, kernel);
        }
    }
}
