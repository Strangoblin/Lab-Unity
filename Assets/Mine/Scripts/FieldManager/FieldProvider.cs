using UnityEngine;

namespace Mine.Fields
{
    /// <summary>Field producer: owns resources; FieldManager owns scheduling and publication.</summary>
    [ExecuteAlways, DefaultExecutionOrder(-900)]
    public abstract class FieldProvider : MonoBehaviour
    {
        /// <summary>Semantic output key. One active provider per key; duplicates are rejected.</summary>
        public abstract string FieldKey { get; }

        /// <summary>Lower runs first. Ties preserve registration order.</summary>
        public virtual int FieldOrder => 0;

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

        // Edit mode only ticks ExecuteAlways.Update on scene repaints, so Inspector edits
        // would otherwise leave the published texture stale until Play mode.
        protected virtual void OnValidate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && IsReady && FieldManager.Instance != null)
            {
                FieldManager.Instance.UpdateFields();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
#endif
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
