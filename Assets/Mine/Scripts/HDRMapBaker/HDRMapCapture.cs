using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ArgumentOutOfRangeException = System.ArgumentOutOfRangeException;

namespace Mine.HDRMaps
{
    /// <summary>Captures scene radiance to six GPU faces and returns one linear HDR panorama readback.</summary>
    public static class HDRMapCapture
    {
        private const float MinimumNearClip = 0.01f;
        private const float MinimumFarClip = 1f;

        public static Texture2D Bake(HDRMapBaker framework, int faceResolution = 256)
        {
            if (framework == null || !framework.IsInitialized)
                throw new UnityException("HDR Map Baker: Select an initialized framework. Use Create / Repair Framework first.");

            return Bake(
                framework.BakeCamera,
                framework.Direction.Origin,
                framework.Direction.transform.rotation,
                CalculateFarClip(framework.Volume, framework.Direction.Origin),
                framework.RenderLayers,
                framework.IncludeSky,
                faceResolution);
        }

        /// <summary>Captures through an explicitly supplied camera; no Camera.main or scene framework lookup is used.</summary>
        public static Texture2D Bake(
            Camera camera,
            Vector3 position,
            Quaternion rotation,
            float farClip,
            int cullingMask,
            bool includeSky,
            int faceResolution = 256)
        {
            if (camera == null)
                throw new UnityException("HDR Map Baker: A dedicated capture camera is required.");
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline))
                throw new UnityException("HDR Map Baker requires the project's Universal Render Pipeline.");
            if (!pipeline.supportsHDR)
                throw new UnityException("HDR Map Baker: Enable HDR on the active URP Asset to preserve radiance above 1.");
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
                throw new UnityException("HDR Map Baker: This device cannot render RGBAHalf targets.");

            int maximumFaceResolution = Mathf.Min(4096, SystemInfo.maxTextureSize / 2);
            if (faceResolution < 16 || faceResolution > maximumFaceResolution)
                throw new ArgumentOutOfRangeException(nameof(faceResolution),
                    $"Face resolution must be between 16 and {maximumFaceResolution} pixels.");
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture cubemap = null;
            GameObject snapshotObject = null;
            Camera snapshot = null;
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            bool previousEnabled = camera.enabled;
            CameraType previousType = camera.cameraType;
            UniversalAdditionalCameraData cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            bool addedCameraData = false;
            AdditionalCameraState cameraDataState = default;
            bool capturedCameraData = false;

            try
            {
                snapshotObject = new GameObject("HDRMap_CameraState") { hideFlags = HideFlags.HideAndDontSave };
                snapshot = snapshotObject.AddComponent<Camera>();
                snapshot.CopyFrom(camera);
                snapshot.enabled = false;

                if (cameraData == null)
                {
                    cameraData = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                    addedCameraData = true;
                }
                cameraDataState = new AdditionalCameraState(cameraData);
                capturedCameraData = true;
                ConfigureCamera(camera, cameraData, cullingMask, includeSky, farClip);

                cubemap = new RenderTexture(
                    faceResolution, faceResolution, 24,
                    RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "HDRMap_CaptureCube",
                    dimension = TextureDimension.Cube,
                    useMipMap = false,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!cubemap.Create())
                    throw new UnityException("HDR Map Baker: Could not allocate the HDR capture cubemap.");

                RenderFaces(camera, cubemap, position, rotation);
                Texture2D panorama = HDRMapConverter.ConvertFromCubemap(
                    cubemap, HDRMapProjection.Equirectangular, faceResolution);
                panorama.name = "HDRMap_Panorama";
                return panorama;
            }
            finally
            {
                if (camera != null)
                {
                    if (snapshot != null)
                        camera.CopyFrom(snapshot);
                    camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                    camera.enabled = previousEnabled;
                    camera.cameraType = previousType;
                }
                if (capturedCameraData && cameraData != null)
                    cameraDataState.Restore(cameraData);
                if (addedCameraData && cameraData != null)
                    DestroyTemporary(cameraData);
                RenderTexture.active = previousActive;
                if (cubemap != null)
                {
                    cubemap.Release();
                    DestroyTemporary(cubemap);
                }
                if (snapshotObject != null)
                    DestroyTemporary(snapshotObject);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Capture state — HDR radiance and alpha are preserved without post processing
        // ════════════════════════════════════════════════════════════
        private static void ConfigureCamera(
            Camera camera,
            UniversalAdditionalCameraData data,
            int cullingMask,
            bool includeSky,
            float farClip)
        {
            camera.enabled = false;
            camera.cameraType = CameraType.Reflection;
            camera.cullingMask = cullingMask;
            camera.clearFlags = includeSky ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.orthographic = false;
            camera.nearClipPlane = MinimumNearClip;
            camera.farClipPlane = Mathf.Max(MinimumFarClip, farClip);
            camera.fieldOfView = 90f;
            camera.aspect = 1f;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.ResetWorldToCameraMatrix();
            camera.ResetProjectionMatrix();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.allowXRRendering = false;
            data.allowHDROutput = false;
        }

        private static void RenderFaces(Camera camera, RenderTexture cubemap, Vector3 origin, Quaternion frame)
        {
            RenderTexture faceTarget = RenderTexture.GetTemporary(cubemap.width, cubemap.height,
                24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            faceTarget.name = "HDRMap_CaptureFace";
            try
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = faceTarget };
                if (!RenderPipeline.SupportsRenderRequest(camera, request))
                    throw new UnityException("HDR Map Baker: The current renderer does not support single-camera requests.");
                for (int face = 0; face < FaceDirections.Length; face++)
                {
                    camera.transform.SetPositionAndRotation(
                        origin, frame * Quaternion.LookRotation(FaceDirections[face], FaceUps[face]));
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    HDRMapConverter.CopyCaptureFace(faceTarget, cubemap, (CubemapFace)face);
                }
            }
            finally { RenderTexture.ReleaseTemporary(faceTarget); }
        }

        public static float CalculateFarClip(HDRMapBakeVolume volume, Vector3 origin)
        {
            if (volume == null)
                return 100f;

            float farClip = MinimumFarClip;
            Vector3 halfSize = volume.Size * 0.5f;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 sign = new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f);
                Vector3 worldCorner = volume.transform.TransformPoint(
                    volume.LocalCenter + Vector3.Scale(halfSize, sign));
                farClip = Mathf.Max(farClip, Vector3.Distance(origin, worldCorner));
            }
            return farClip * 1.05f;
        }

        private static void DestroyTemporary(Object target)
        {
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }

        private readonly struct AdditionalCameraState
        {
            private readonly CameraRenderType _renderType;
            private readonly bool _postProcessing;
            private readonly AntialiasingMode _antialiasing;
            private readonly bool _xr;
            private readonly bool _hdrOutput;

            public AdditionalCameraState(UniversalAdditionalCameraData data)
            {
                _renderType = data.renderType;
                _postProcessing = data.renderPostProcessing;
                _antialiasing = data.antialiasing;
                _xr = data.allowXRRendering;
                _hdrOutput = data.allowHDROutput;
            }

            public void Restore(UniversalAdditionalCameraData data)
            {
                data.renderType = _renderType;
                data.renderPostProcessing = _postProcessing;
                data.antialiasing = _antialiasing;
                data.allowXRRendering = _xr;
                data.allowHDROutput = _hdrOutput;
            }
        }

        private static readonly Vector3[] FaceDirections =
        {
            Vector3.right, Vector3.left, Vector3.up,
            Vector3.down, Vector3.forward, Vector3.back
        };

        private static readonly Vector3[] FaceUps =
        {
            Vector3.up, Vector3.up, Vector3.back,
            Vector3.forward, Vector3.up, Vector3.up
        };
    }
}
