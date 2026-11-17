using UnityEngine;

namespace Mine.Interaction
{
    /// <summary>
    /// 交互处理器契约 — 消费 Manager 共享的 originRT，独立管理自己的输出 RT，
    /// 并把结果发布成全局 Shader 属性。
    ///
    /// 推荐直接继承 <see cref="InteractionProcessor"/>：它负责注册、生命周期与
    /// 纹理重绑，实现者只需要写 CreateResources / ProcessStep / BindGlobals。
    /// </summary>
    public interface IUniversalInteractionProcessor
    {
        /// <summary>语义通道名（"water" / "grass" / "snow" / "sand"）。同一时间只允许一个同名处理器。</summary>
        string InteractionKey { get; }

        /// <summary>越小越先处理；相同顺序保留注册顺序。</summary>
        int InteractionOrder { get; }

        /// <summary>资源是否已创建并绑定到 Manager 的输入 RT。</summary>
        bool IsReady { get; }

        /// <summary>创建输出 RT 并绑定到 Manager 管理的共享输入 RT (originRT)。</summary>
        void Initialize(int resolution, RenderTexture sourceRT);

        /// <summary>每帧处理：设置参数并 Dispatch Compute Shader。</summary>
        /// <param name="deltaTime">帧间隔时间</param>
        /// <param name="worldDelta">物体世界空间位移 (XZ)，用于 RT 数据重投影</param>
        void Process(float deltaTime, Vector2 worldDelta);

        /// <summary>把输出 RT 绑定为全局 Shader 属性，供渲染 Shader 采样。</summary>
        void BindGlobalTextures();

        /// <summary>撤销全局属性（处理器禁用 / 被 Manager 卸载时调用）。</summary>
        void ResetGlobals();

        /// <summary>释放自管理的 RT。</summary>
        void Release();
    }
}
