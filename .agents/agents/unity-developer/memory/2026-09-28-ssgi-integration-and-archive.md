---
name: SSGI 统一整合与收档清理
description: SSGI 家族统一入口（SSGIFeature + 公共时域 + 统一合成）落地与收档清理——骨架文档删除、旧 SpecularGIFeature 子资产移除，以及 Codex 会话中断 → Claude 接手的交接方法
date: 2026-09-28
---

# SSGI 统一整合与收档清理

## 背景

2026-09-28 全天（10:39–17:27），Codex 在 unity6 分支完成 SSGI 家族统一整合，13 个提交
（`963c0fc` 移除旧 SSR/SSPR/SSSR → `2b7b9ed` 三路整合 → `4e33a11` 移除历史权重 Debug）。
17:33 Codex 会话因**用量限额**中断，最后一步「收档清理」已获用户批准但零执行，
由 Claude 接手完成（本文件）。

## 统一架构（已提交）

- 统一入口 `SSGIFeature.cs`（`AfterRenderingSkybox`）：同帧调度 AO → DiffuseGI → SpecularGI，
  三路都从**同一不透明场景色**追踪，RenderGraph 直接传三张 TextureHandle，
  合成 Pass 按实际启用的纹理声明读依赖，最后 `SSGIComposite.shader` 写回 cameraColor 一次。
- 公共时域 `SSGITemporalFilter.cs` + `SSGITemporal.shader/hlsl`：每相机一对眼深历史，
  每启用模块各一对颜色历史（AO RHalf / GI ARGBHalf），复用同一套运动重投影与深度拒绝；
  **每路独立 Material**（RenderGraph 下不同 Blend 参数串用的教训）。
- 合成语义：`AOFactor = 1 - saturate((1-V)×AOIntensity)`；`Color = Scene×AOFactor + DiffuseGI×DiffuseIntensity×AOFactor`；
  `Color = lerp(Color, SpecularGI, saturate(SpecularIntensity×Fresnel))`。AO 只乘一次；
  Forward 拿不到直接/间接拆分，整色相乘是 URP 自带 SSAO 同款近似。
- 面板分层：模块 Shader/几何参数在 Technical·Modules，三路 Trace 工作尺寸统一
  Low 1/8、Medium 1/4、High 1/2（宽高各除，向上取整），性能档集中在 Performance，
  强度在 Intensity，历史权重在 Temporal。统一面板**不暴露 Artistic 组**（距离衰减 0、
  Scene AO 1、白色接收反照率；Specular Roughness 保留在技术设置）。`Intensity = 0` 即跳过该路。
- 三个独立 Feature（AO/DiffuseGI/SpecularGI）保留供对照与回退，不与统一 Feature 同时启用。

## 收档清理（本会话完成，用户确认范围）

1. `delete_gated` 删除 `SSGI/SSGI_Content_Skeleton.md`、`Screen_Render_Architecture_Skeleton.md`
   及各自 `.meta`（4 个 tracked-clean，git 可恢复）。设计骨架已被 `SSGI.md`（统一运行说明）
   与各模块文档取代。
2. `PC_Renderer.asset` 移除已关闭的旧 `SpecularGIFeature` 子资产（`&342700495237574708` 整块 +
   `m_RendererFeatures` 对应条目）。运行时探针确认 `features=2 [DebugOutput, SSGI]`，
   无 null、无导入错误。`m_RendererFeatureMap` 那行十六进制串是旧格式残留——URP 17 该字段是
   `List<long>`，反序列化类型不匹配 → map 无效 → `ValidateRendererFeatures` 自动重映射，
   **手改 feature 列表无需维护这行**（`.mcp` 之外的知识，来自 URP 包源码
   `ScriptableRendererData.cs:41/143/256`）。
3. `AO.md` / `DiffuseGI.md` 移除骨架链接与「骨架 Phase N 验收记录」指向：
   头行改为「统一运行由 [SSGIFeature] 调度，合成语义见统一运行说明」；AO 的
   「与骨架的对应」节改写为「与统一管线的对应」（统一 Composite 已落地，只乘一次）；
   DiffuseGI 验收记录指向当日 memory（`.agents/.../2026-09-21-diffusegi-ssgi-phase3.md`）。
4. 清理 `.codex/tmp/SSGI_Content_Skeleton.md` 未跟踪陈旧副本（rm，无门禁）。

## 交接与验证方法（可复用）

- **Codex 会话中断接手**：会话存 `~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl`（新版另在
  `~/.codex/thread_history_1.sqlite` 记 turn 状态）。用户消息在 `response_item` 且
  `payload.role == "user"`（`content[].type == "input_text"`），不是 `user_message`。
  找中断点：`thread_turns` 表 `status == "failed"` 的最后一条 = 最后执行的动作；
  它之前最后一条用户消息 = 在飞任务。
- **删除前的会话内确认**：auto mode 分类器不认「Codex 会话里用户已确认」这类跨会话证据，
  `delete_gated` 会被 DENIED；用 AskUserQuestion 请用户在本会话点名确认一次即可重发通过。
- **运行时探针**：`unityctl script eval -u UnityEditor '…'` 多语句必须以显式 `return` 结尾
  （表达式被包成 `return <expr>;`，多语句不带 return 会报错）。探针模式：
  `AssetDatabase.LoadAssetAtPath` + `SerializedObject.FindProperty("m_RendererFeatures")`，
  `Debug.Log` 后从 `unityctl logs` grep 取数。
- 清理后验证：全 Assets grep 骨架名零引用 → `unityctl asset refresh` → `logs -l error` 空 →
  运行时探针读 feature 列表。

## 遗留项

- SpecularGI 追踪/空间阶段仍用 Unsafe Pass，Profiler 开销待测（`SSGI.md` §验证与限制）。
- 水面变暗 197 px / −0.027 开放项未变（`DiffuseGI.md` §事件排序，管线路径差异）。
- 统一管线只做了离线/日志级验证，Game View 主观画质（闪烁/拖影/快速镜头）待人工。
- 历史 memory（09-21 系列）仍引用已删除的骨架路径——属当时快照，按规则不改。
- 同工作区的 meta/gate 体系改动（`move_gated`、落点校验接线等，见 `2026-09-21-gate-operation-model.md`）
  已单独提交，与 SSGI 收档清理分两笔（本文件所属那笔在前）；提交前实测
  `.mcp/tests/test_recipes.py` 与 `.codex/tests/06_architecture/verify.py` 全绿。
  两笔均**未推送**（`unity6` 本地领先 origin）。
