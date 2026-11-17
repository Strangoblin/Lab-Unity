using UnityEngine;

namespace Mine.Interaction
{
    /// <summary>
    /// 交互处理器插件基类 — 与 <c>FieldProvider</c> / <c>IUniversalInstanceSimulator</c> 同构。
    ///
    /// 每个子类是一个独立子集（水面 / 草地 / 雪地 / 沙地 …），自己拥有输出 RT；
    /// <see cref="UniversalInteractionManager"/> 只提供共享的正交捕获输入并按顺序驱动它们。
    /// 启用时自动注册到 Manager（重复通道名会被拒绝），禁用时自动释放。
    /// </summary>
    // 没有 ExecuteAlways 时，编辑器加载场景并不会调用普通 MonoBehaviour 的 OnEnable，
    // 处理器就不会注册，Manager 的编辑模式预览永远是空的。
    [ExecuteAlways]
    public abstract class InteractionProcessor : MonoBehaviour, IUniversalInteractionProcessor
    {
        /// <summary>语义通道名。同一时间只允许一个同名处理器。</summary>
        public abstract string InteractionKey { get; }

        /// <summary>越小越先处理。Ties preserve registration order.</summary>
        public virtual int InteractionOrder => 0;

        public bool IsReady { get; private set; }

        /// <summary>Manager 使用的 RT 分辨率。</summary>
        public int Resolution { get; private set; }

        /// <summary>Manager 管理的共享输入 RT（正交相机捕获的穿透深度）。</summary>
        protected RenderTexture SourceRT { get; private set; }

        // ════════════════════════════════════════════════════════════
        //  注册 / 生命周期
        // ════════════════════════════════════════════════════════════

        protected virtual void OnEnable()
        {
            if (UniversalInteractionManager.Register(this))
                return;

            Teardown();
            enabled = false;
        }

        protected virtual void OnDisable()
        {
            UniversalInteractionManager.Unregister(this);
            Teardown();
            ResetGlobals();
        }

        // ════════════════════════════════════════════════════════════
        //  IUniversalInteractionProcessor — Manager 驱动入口
        // ════════════════════════════════════════════════════════════

        public void Initialize(int resolution, RenderTexture sourceRT)
        {
            if (!isActiveAndEnabled || sourceRT == null || resolution <= 0)
                return;

            if (Resolution != resolution || SourceRT != sourceRT)
            {
                Teardown();
                Resolution = resolution;
                SourceRT = sourceRT;
            }

            if (!IsReady)
            {
                IsReady = CreateResources();
                if (!IsReady)
                    return;
            }

            BindTextures();
            BindGlobalTextures();
        }

        public void Process(float deltaTime, Vector2 worldDelta)
        {
            if (!IsReady)
                return;

            // 每次 Dispatch 前重绑：compute 资产重新导入后 shader 实例会重建、旧绑定静默失效。
            BindTextures();
            ProcessStep(Mathf.Max(deltaTime, 0f), worldDelta);
        }

        public void BindGlobalTextures()
        {
            if (IsReady)
                BindGlobals();
        }

        public void Release()
        {
            Teardown();
        }

        /// <summary>Manager 关闭时调用：释放资源但保留注册，Manager 重新启用后会再次 Initialize。</summary>
        internal void Detach()
        {
            Teardown();
            ResetGlobals();
        }

        // ════════════════════════════════════════════════════════════
        //  子类实现
        // ════════════════════════════════════════════════════════════

        /// <summary>创建输出 RT / 查找 kernel。返回 false 表示资源不完整，本帧不参与处理。</summary>
        protected abstract bool CreateResources();

        /// <summary>把 ComputeShader 的纹理槽重新指向当前 RT（每帧调用）。</summary>
        protected abstract void BindTextures();

        /// <summary>推进一帧模拟。基类已完成纹理重绑。</summary>
        protected abstract void ProcessStep(float deltaTime, Vector2 worldDelta);

        /// <summary>发布全局 Shader 属性。</summary>
        protected abstract void BindGlobals();

        public abstract void ResetGlobals();

        /// <summary>释放自管理资源；必须可重复调用。</summary>
        protected abstract void ReleaseResources();

        // ════════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════════

        private void Teardown()
        {
            if (!IsReady && SourceRT == null && Resolution == 0)
                return;

            ReleaseResources();
            IsReady = false;
            SourceRT = null;
            Resolution = 0;
        }

        protected static RenderTexture CreateRT(string name, int resolution)
        {
            var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RFloat)
            {
                name              = name,
                filterMode        = FilterMode.Bilinear,
                wrapMode          = TextureWrapMode.Clamp,
                useMipMap         = false,
                autoGenerateMips  = false,
                enableRandomWrite = true,
            };
            rt.Create();
            return rt;
        }

        protected static void ClearRT(RenderTexture rt)
        {
            if (rt == null) return;
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = previous;
        }

        protected static void ReleaseRT(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            if (Application.isPlaying) Object.Destroy(rt);
            else                       Object.DestroyImmediate(rt);
            rt = null;
        }

        /// <summary>世界位移 → 像素偏移（旧网格中同一世界位置的像素坐标 = 当前 + 偏移）。</summary>
        protected static Vector2Int WorldDeltaToPixels(Vector2 worldDelta, int resolution, float areaSize)
        {
            float scale = resolution / Mathf.Max(areaSize, 1e-4f);
            return new Vector2Int(
                Mathf.RoundToInt(worldDelta.x * scale),
                Mathf.RoundToInt(worldDelta.y * scale));
        }

        // ════════════════════════════════════════════════════════════
        //  域边界淡出
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 淡出带宽度（占 RT 边长的比例）。与 InteractionDomainEdge.hlsl 的
        /// INTERACTION_DOMAIN_FADE 必须一致；淡出约定和成因见那个文件。
        /// </summary>
        protected const float DomainFadeWidth = 0.08f;

        /// <summary>
        /// 穿过整条淡出带的总吸收系数：指数上是 k·∫(1−w) ≈ k/2 = 6，即 e^-6 ≈ 0.25%。
        /// 再大痕迹收得更干净，但淡出带被压窄、纹理级台阶变陡（k = 32 时台阶反而从 16% 涨到 21%）。
        /// </summary>
        private const float kDomainBandAbsorb = 12f;

        /// <summary>
        /// 边界吸收量（供 compute 的 <c>_EdgeAbsorb</c>）：按**这一步穿过的淡出带比例**折算，
        /// 不是按时间。域不动时恒为 0，痕迹一点都不会变（这正是「静置一会儿软边变硬边」的修法）；
        /// 域把痕迹扫向边界时按位移累积，数据被真的吃掉 —— 所以域再扫回来也不会露出硬断口。
        /// </summary>
        protected Vector2 EdgeAbsorbPerStep(Vector2 worldDelta, float areaSize)
        {
            float bandWorld = Mathf.Max(areaSize * DomainFadeWidth, 1e-4f);
            float scale = kDomainBandAbsorb / bandWorld;
            return new Vector2(Mathf.Abs(worldDelta.x), Mathf.Abs(worldDelta.y)) * scale;
        }
    }
}
