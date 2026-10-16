# DiffuseGI 采样序列与地面残留

2026-10-14 实施；2026-10-15 按新 memory 路由接续归档。此前用户要求架构重构期间先临时记录，原始接续在 `tmp/sessions/2026-10-14-diffuse-sequence-r1/staging/sequence-memory.md`；本条取代其待归档状态，不重建旧角色内 memory 目录。

## 决定与实现

- 用户允许优化序列，目标是静止镜头、时缓 0.5 下的 Diffuse 抖动；当时性能比较后置。
- 沿用完整 cosine 半球、黑色 miss/总射线平均、既有射线/步进/分辨率、两级 1→2 重建及原时域；不冻结采样、不提高时缓，不改 Specular/AO。
- `DiffuseGIFunction.hlsl` 用 8×8 块内 Morton 顺序映射 Sobol 两维前 64 点；块级平移复用 `Hash_FracGrid22`，保持逐帧 R2 平移。径向改为每条射线区间内抖动，方位保留黄金角分布，不宣称蓝噪声/STBN。
- Diffuse 历史 `_sampling.w=3` 是采样实现版本；拒绝旧单遍 `w=0` 与旧序列 `w=2`，仅本通道失效、不重分配 RT。

## 已验证

- Metal 正向 67 组 / 1,152,825 断言及四个负控制通过；Specular 三档冻结 RGB 回归差 0。复跑入口为 `Assets/Mine/Shaders/PostProcess/SSGI/Validation~/run.sh --sequence`。
- 独立 CPU float32 三档 × 128 帧 × 128² 网格检查：范围、径向分层、cosine 均值、空间数字网与持续变化通过；部分 Low/High 纯径向 horizon 合成项反而更差，不保证所有被积函数改善。
- 固定复制静止视角、640×360、Medium/DDA、时缓 0.5、预热 32 请求、采样帧 32–95；Trace 原生 160×90。A/B/A 都使用当前两级 Feature，只有基线采样 helper 冻结。
- 原生 Trace 亮度帧差 RMS `0.0276067393 → 0.0236816167`（−14.22%），时间方差 −18.28%；持久 history RMS `0.0026292862 → 0.0022240074`（−15.41%）、时间方差 −31.90%、平均亮度 +0.10%。
- 各帧表面 mask、预算、射线帧、有效历史与 Shader 时间匹配；重复基线存在微小非逐位差异，聚合量及按均值归一化的均值图 RMS 漂移受 0.2% 界限检查，不宣称 bitwise 确定性。
- 原生 Trace 在同帧通过 RenderGraph 读依赖导出到自有 RTHandle，history/depth 读生产持久纹理，不裸读帧末回收 global。

## 人工反馈与限制

- 用户反馈：**墙壁良好，地面仍明显抖动**。全图 15.4% 改善不代表地面解决，也不能与前一轮两级重建百分比相加。
- 地面透视深度差可能削弱 guide 有效覆盖、稀疏低仰角命中可能保留采样方差：均为未分区验证的假设，不写成根因。
- 只有一个静止视角；运动拖影、局部 floor 指标、XR/其它 API、性能均不在本轮证据内。后续用户改为允许细节取舍、优先 Diffuse 性能，见 2026-10-15 High 再平衡记录。
- 未提交/推送，未保存原场景/Renderer，原脏改动保持。此前 OpenCode 源码外发被拒、内置 worker 容量失败，主会话完成；没有借新权限重试被拒的外发。

## 证据与复现边界

必要报告、四张均值图与采样报告归档于 [evidence/2026-10-14-diffuse-sequence](evidence/2026-10-14-diffuse-sequence/)。代码、旧采样输入、checker/runner 留在 SSGI `Validation~/`，不归档整包临时脚本。旧完整窗口仍在 `tmp/sessions/2026-10-14-diffuse-sequence-ab-65a663b1/output`。

历史输入是当时 live 活动场景，不是完整冻结场景资产；报告记录视角与设置。复跑需要对应场景/Shader/设置，不能保证重现绝对数值，PNG 也不是线性 HDR 数值 oracle。证据归 Unity 角色维护，功能退役后才按精确清单授权清理。
