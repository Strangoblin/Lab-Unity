using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Mine.HDRMaps
{
    /// <summary>
    /// GPU projection conversion. The source may be unreadable; the returned RGBAHalf
    /// linear Texture2D is readable and owned by the caller. Source assets are never changed.
    /// </summary>
    public static class HDRMapConverter
    {
        private const string ConversionShaderName = "Hidden/Mine/HDRMapConvert";
        private const int MinimumResolution = 16;
        private const int MaximumResolution = 4096;

        /// <summary>Resolution denotes panorama height or the side of a square projection.</summary>
        public static Vector2Int GetOutputSize(HDRMapProjection projection, int resolution)
        {
            ValidateProjection(projection);
            if (resolution < MinimumResolution || resolution > MaximumResolution)
                throw new ArgumentOutOfRangeException(nameof(resolution), "Resolution must be between 16 and 4096.");
            int width = projection == HDRMapProjection.Equirectangular ? resolution * 2 : resolution;
            if (width > SystemInfo.maxTextureSize || resolution > SystemInfo.maxTextureSize)
                throw new ArgumentOutOfRangeException(nameof(resolution), "The output exceeds this device's maximum texture size.");
            return new Vector2Int(width, resolution);
        }

        public static Texture2D Convert(
            Texture source,
            HDRMapProjection sourceProjection,
            HDRMapProjection targetProjection,
            int resolution,
            HDRMapCoverage coverage = HDRMapCoverage.Panorama)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source.dimension != TextureDimension.Tex2D)
                throw new ArgumentException("Use ConvertFromCubemap for a cubemap source.", nameof(source));
            ValidateProjection(sourceProjection);
            return ConvertInternal(source, false, sourceProjection, targetProjection, resolution, coverage);
        }

        /// <summary>Accepts both Cubemap assets and cube RenderTextures without CPU face readback.</summary>
        public static Texture2D ConvertFromCubemap(
            Texture source,
            HDRMapProjection targetProjection,
            int resolution,
            HDRMapCoverage coverage = HDRMapCoverage.Panorama)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source.dimension != TextureDimension.Cube)
                throw new ArgumentException("The source must have TextureDimension.Cube.", nameof(source));
            return ConvertInternal(source, true, HDRMapProjection.Equirectangular, targetProjection, resolution, coverage);
        }

        private static Texture2D ConvertInternal(
            Texture source,
            bool sourceIsCube,
            HDRMapProjection sourceProjection,
            HDRMapProjection targetProjection,
            int resolution,
            HDRMapCoverage coverage)
        {
            Vector2Int size = GetOutputSize(targetProjection, resolution);
            if (coverage != HDRMapCoverage.Panorama && coverage != HDRMapCoverage.Interior)
                throw new ArgumentOutOfRangeException(nameof(coverage));
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
                throw new NotSupportedException("HDR conversion requires ARGBHalf rendering and RGBAHalf textures.");
            Shader shader = Shader.Find(ConversionShaderName);
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("The HDRMap conversion shader is missing or unsupported.");

            Material material = null;
            RenderTexture target = null;
            CommandBuffer commands = null;
            Texture2D result = null;
            RenderTexture previousActive = RenderTexture.active;
            bool previousSRGBWrite = GL.sRGBWrite;
            try
            {
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetInt("_SourceIsCube", sourceIsCube ? 1 : 0);
                material.SetInt("_SourceProjection", (int)sourceProjection);
                material.SetInt("_TargetProjection", (int)targetProjection);
                material.SetInt("_Coverage", (int)coverage);
                material.SetTexture(sourceIsCube ? "_SourceCube" : "_Source2D", source);
                target = RenderTexture.GetTemporary(size.x, size.y, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                target.name = "HDRMap_ConversionTarget";
                GL.sRGBWrite = false;
                // Follow the project's procedural fullscreen draw rather than depending
                // on Graphics.Blit's compatibility-mode mesh attributes or matrices.
                commands = new CommandBuffer { name = "HDR Map Conversion" };
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(false, true, Color.clear);
                commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(commands);

                result = new Texture2D(size.x, size.y, TextureFormat.RGBAHalf, false, true)
                {
                    name = $"{source.name}_{targetProjection}_{coverage}",
                    filterMode = FilterMode.Bilinear,
                    wrapModeU = targetProjection == HDRMapProjection.Equirectangular ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                    wrapModeV = TextureWrapMode.Clamp,
                    wrapModeW = TextureWrapMode.Clamp
                };
                RenderTexture.active = target;
                result.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0, false);
                result.Apply(false, false);
                return result;
            }
            catch
            {
                if (result != null)
                    DestroyTemporary(result);
                throw;
            }
            finally
            {
                GL.sRGBWrite = previousSRGBWrite;
                RenderTexture.active = previousActive;
                if (target != null)
                    RenderTexture.ReleaseTemporary(target);
                commands?.Dispose();
                if (material != null)
                    DestroyTemporary(material);
            }
        }

        /// <summary>Copies a rendered face into hardware cubemap orientation on the GPU.</summary>
        public static void CopyCaptureFace(RenderTexture source, RenderTexture cube, CubemapFace face)
        {
            Shader shader = Shader.Find(ConversionShaderName);
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("The HDRMap conversion shader is missing or unsupported.");
            Material material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            CommandBuffer commands = new CommandBuffer { name = "HDR Map Cube Face" };
            bool previousSRGBWrite = GL.sRGBWrite;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                material.SetTexture("_Source2D", source);
                GL.sRGBWrite = false;
                commands.SetRenderTarget(cube, 0, face);
                commands.SetViewport(new Rect(0f, 0f, cube.width, cube.height));
                commands.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(commands);
            }
            finally
            {
                GL.sRGBWrite = previousSRGBWrite;
                RenderTexture.active = previousActive;
                commands.Dispose();
                DestroyTemporary(material);
            }
        }

        private static void ValidateProjection(HDRMapProjection projection)
        {
            if ((int)projection < (int)HDRMapProjection.Equirectangular || (int)projection > (int)HDRMapProjection.Hemisphere)
                throw new ArgumentOutOfRangeException(nameof(projection));
        }

        private static void DestroyTemporary(Object value)
        {
            if (Application.isPlaying)
                Object.Destroy(value);
            else
                Object.DestroyImmediate(value);
        }
    }
}
