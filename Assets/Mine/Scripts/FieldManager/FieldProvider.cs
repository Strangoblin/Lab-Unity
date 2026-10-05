using UnityEngine;

namespace Mine.Fields
{
    public enum FieldKind { Wind, Wave, Acceleration }

    /// <summary>Field producer: owns resources; FieldManager owns scheduling and publication.</summary>
    [ExecuteAlways, DefaultExecutionOrder(-900)]
    public abstract class FieldProvider : MonoBehaviour
    {
        public abstract FieldKind Kind { get; }
        public uint Version { get; private set; }
        public float SampleTime { get; private set; }
        public bool IsReady { get; private set; }

        // ════════════════════════════════════════════════════════════
        //  Lifecycle — register one producer per semantic output
        // ════════════════════════════════════════════════════════════
        protected virtual void OnEnable()
        {
            if (!InitializeField()) { enabled = false; return; }
            IsReady = true;
            if (!FieldManager.Register(this))
            {
                ReleaseField();
                IsReady = false;
                enabled = false;
                return;
            }
            Advance(FieldManager.Clock, 0f);
        }

        protected virtual void Update()
        {
            if (IsReady && !FieldManager.HasActiveManager)
                Advance(FieldManager.Clock, Time.deltaTime);
        }

        protected virtual void OnDisable()
        {
            if (!IsReady) return;
            FieldManager.Unregister(this);
            ReleaseField();
            IsReady = false;
        }

        public void Advance(float time, float deltaTime)
        {
            if (!IsReady) return;
            SimulateField(time, Mathf.Max(0f, deltaTime));
            SampleTime = time;
            Version++;
            PublishField();
        }

        protected abstract bool InitializeField();
        protected abstract void SimulateField(float time, float deltaTime);
        protected abstract void PublishField();
        public abstract void ResetGlobals();
        public abstract void BindCompute(ComputeShader shader, int kernel);
        protected abstract void ReleaseField();
    }
}
