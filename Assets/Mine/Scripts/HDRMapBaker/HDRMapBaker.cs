using UnityEngine;

namespace Mine.HDRMaps
{
    /// <summary>Owns an HDR panorama capture frame and selects which scene content is rendered.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class HDRMapBaker : MonoBehaviour
    {
        [Header("Capture Content")]
        [Tooltip("GameObject layers rendered by the dedicated capture camera. Nothing + Include Sky captures only the sky.")]
        [SerializeField] private LayerMask _renderLayers = -1;
        [SerializeField] private bool _includeSky = true;

        [Header("Generated Components")]
        [SerializeField, HideInInspector] private HDRMapBakeVolume _volume;
        [SerializeField, HideInInspector] private HDRMapBakeDirection _direction;
        [SerializeField, HideInInspector] private Camera _camera;

        public HDRMapBakeVolume Volume => _volume;
        public HDRMapBakeDirection Direction => _direction;
        public Camera BakeCamera => _camera;
        public int RenderLayers => _renderLayers.value;
        public bool IncludeSky => _includeSky;

        public bool IsInitialized => Owns(_volume) && Owns(_direction) && Owns(_camera);

        // ════════════════════════════════════════════════════════════
        //  Frame synchronization — only the framework's dedicated camera is controlled
        // ════════════════════════════════════════════════════════════
        public void SyncBakeCamera()
        {
            if (!Owns(_camera) || !Owns(_direction))
                return;

            _camera.transform.SetPositionAndRotation(_direction.Origin, _direction.transform.rotation);
            _camera.cameraType = CameraType.Reflection;
            _camera.enabled = false;
        }

        public bool Owns(Component component)
        {
            return component != null
                && component.transform.parent == transform
                && component.GetComponentInParent<HDRMapBaker>() == this;
        }
    }
}
