using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Mine.Interaction
{
    /// <summary>
    /// Demo-only driver: glides the shared interactor between two world points so the
    /// interaction camera (which follows the same transform) sweeps the surface patches.
    /// Works in edit mode as well, so the InteractionManager preview has motion to show.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Mine/Interaction/Interaction Probe Driver")]
    public sealed class InteractionProbeDriver : MonoBehaviour
    {
        [SerializeField] private Vector3 _start = new Vector3(-14f, 0.3f, 0f);
        [SerializeField] private Vector3 _end   = new Vector3(14f, 0.3f, 0f);
        [Tooltip("Round trips per second. 0.12 ≈ 8 s per one-way pass.")]
        [SerializeField, Min(0f)] private float _speed = 0.12f;
        [Tooltip("Sideways sway amplitude in metres.")]
        [SerializeField, Min(0f)] private float _sway = 1.2f;

        private float _phase;
        private double _lastEditorTime;

        private void OnEnable()
        {
            Apply();
#if UNITY_EDITOR
            EditorApplication.update += EditorUpdate;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= EditorUpdate;
#endif
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            EditorApplication.update -= EditorUpdate;
#endif
        }

        private void Update()
        {
            if (Application.isPlaying)
                Advance(Time.deltaTime);
        }

        private void Advance(float deltaTime)
        {
            if (_speed <= 0f)
                return;

            _phase += Mathf.Max(deltaTime, 0f) * _speed;
            Apply();
        }

        private void Apply()
        {
            float t = Mathf.PingPong(_phase, 1f);
            Vector3 position = Vector3.Lerp(_start, _end, t);
            position.z += Mathf.Sin(_phase * Mathf.PI * 2f) * _sway;
            transform.position = position;
        }

#if UNITY_EDITOR
        private void EditorUpdate()
        {
            // 场景关闭时销毁发生在 EditorApplication.update 的遍历中，
            // 本帧已经排队到这里的回调仍会执行一次 —— 先确认自己还活着。
            if (this == null || Application.isPlaying)
                return;

            double now = EditorApplication.timeSinceStartup;
            if (_lastEditorTime <= 0d)
            {
                _lastEditorTime = now;
                return;
            }

            float deltaTime = Mathf.Min((float)(now - _lastEditorTime), 0.1f);
            _lastEditorTime = now;
            Advance(deltaTime);
        }
#endif
    }
}
