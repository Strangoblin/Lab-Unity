# Meta Developer — Memory Index

> 按日期命名的 memory 文件索引。每次修改共享 agent 体系或平台适配层后追加。

---

## Memory 文件

| 文件 | 日期 | 摘要 |
|------|------|------|
| [2026-09-21-spatial-filter-budget-guidance.md](2026-09-21-spatial-filter-budget-guidance.md) | 2026-09-21 | **空间滤波参数契约与模板**：Quality 固定样本数/分布/下采样率，Radius 只缩放 offset 和感受野；覆盖 Blur/SNN/Kuwahara/Bilateral/SSGI Resolve，修正 fullscreen 旧表述并新增 9/16/25 tap HLSL 骨架。 |
| [2026-09-21-delete-gated.md](2026-09-21-delete-gated.md) | 2026-09-21 | 删除通道 delete_gated — Assets/Mine 删除被封死的真因是**权限路径规则连带命中 Bash 的 rm/mv**（而非 `rm -rf` deny；且 `Write(path)` 是死规则、deny 不可被 allow/hook 覆盖）；补删除闸（不可恢复性判定）而非放松 deny；**MCP + Bash CLI 两通道判定同源**、审计落盘 `.mcp/deletes.jsonl`；附「MCP 工具表按会话冻结、重启进程不刷新」 |
| [2026-09-20-parameter-panel-template.md](2026-09-20-parameter-panel-template.md) | 2026-09-20 | 面板参数分层（Technical/Performance/Artistic/Debug）入 standard 规范 + 骨架；实测暴露模板从未被验过（2 个 .cs 带真实 API 错误 + 1 个 .shader 带 emoji 标识符），据此把「模板可编译」重定义为「模板 API 可核验」并落地 `check_api_refs.py`，同时纠正一条写反的 VolumeComponent 规范 |
| [2026-09-17-urp17-material-templates.md](2026-09-17-urp17-material-templates.md) | 2026-09-17 | Unity 6/URP 17 材质模板升级：实例化、Stereo、四 Pass 一致性及能力契约 |
| [2026-09-17-debug-validation-route.md](2026-09-17-debug-validation-route.md) | 2026-09-17 | 2D 验证对齐 PostProcess/Debug 资源与材质优先的 DebugOutputFeature |
| [2026-09-17-shader-validation-branches.md](2026-09-17-shader-validation-branches.md) | 2026-09-17 | 2D 全屏与 3D 网格验证并列接入 compile/runtime；明确隔离渲染与证据边界 |
| [2026-09-17-shader-menu-naming.md](2026-09-17-shader-menu-naming.md) | 2026-09-17 | 固化 Mine Shader 菜单路径命名，同步 Shader/Baker 模板与规范索引 |
| [2026-09-14-mine-write-scope.md](2026-09-14-mine-write-scope.md) | 2026-09-14 | Unity 门禁可写范围统一为 Assets/Mine；补充目录边界和软链越界回归测试 |
| [2026-09-10-meta-mcp-bypass.md](2026-09-10-meta-mcp-bypass.md) | 2026-09-10 | meta-developer 与体系路径绕过 Unity MCP；g_entry 收紧为 unity-developer，并新增边界回归测试 |
| [2026-09-10-unity-cli-routing.md](2026-09-10-unity-cli-routing.md) | 2026-09-10 | unity-editor skill 增加官方 Editor CLI 与 unityctl 的选择边界、同项目双实例安全约束及适配同步说明 |
| [2026-09-04-agent-architecture-cutover.md](2026-09-04-agent-architecture-cutover.md) | 2026-09-04 | 架构解耦收尾 P0-P3 + skills 软链切流（已执行 fdf9106，人工确认） — 最终验收全绿（fixture phase7） |
| [2026-09-04-codex-compatibility-links.md](2026-09-04-codex-compatibility-links.md) | 2026-09-04 | Codex 适配层补齐指向 `.agents/` 的相对软链，并加入架构契约测试 |
| [2026-09-04-codex-agent-temp-cleanup.md](2026-09-04-codex-agent-temp-cleanup.md) | 2026-09-04 | 退役 Codex exec/auto 替代 agent，清理临时产物并将长期知识归档到共享 memory |
| [2026-09-01-gate-state-persistence.md](2026-09-01-gate-state-persistence.md) | 2026-09-01 | NO_RECIPE 根因 = MCP server 空闲重启清空进程内状态；修复 = state.json 持久化 + 原子写 + 启动恢复 + reset 清文件 |
| [2026-09-03-unityctl-bridge-host-isolation.md](2026-09-03-unityctl-bridge-host-isolation.md) | 2026-09-03 | UnityCtl bridge 宿主/网络命名空间隔离诊断与共享宿主恢复流程 |
| [2026-09-04-interior-map-window-placement.md](2026-09-04-interior-map-window-placement.md) | 2026-09-04 | InteriorMap Baker Window 保留 GUID 归位功能 Editor 目录，并同步 window/baker 模板范例路径 |
| [2026-09-04-fgd-window-placement.md](2026-09-04-fgd-window-placement.md) | 2026-09-04 | FGD LUT Baker Window 保留 GUID 归位功能 Editor 目录，并清除最后一个全局 EditorWindow 遗留路径 |
| [2026-09-03-template-conventions-promoted.md](2026-09-03-template-conventions-promoted.md) | 2026-09-03 | 模板/库编写规则提升为活动规范 — layer-conventions Template 层刷新 + 新增 template-conventions.md 细则 + 索引 |
| [2026-09-03-template-reference-taxonomy.md](2026-09-03-template-reference-taxonomy.md) | 2026-09-03 | 模板与知识库按实际职责重组：standard / script / shader，后处理允许混合 .cs/.shader/.compute/.md |
| [2026-08-27-feature-screen-debug-retired.md](2026-08-27-feature-screen-debug-retired.md) | 2026-08-27 | feature-screen-debug 临时脚本/引用退役 → 固定 DebugOutputFeature；屏幕调试方法并入 script-structure.md；SSL Volume 方法归档 volume-component.md |
| [2026-08-25-gate-consequence-verification.md](2026-08-25-gate-consequence-verification.md) | 2026-08-25 | 门禁收敛为后果验证 v2 — 链统一 [g_entry, g_knowledge]、知识证据真实文件校验、write_gated 内容规范检查、Codex 经 check_norm CLI 对等 |
| [2026-08-25-codex-direct-read.md](2026-08-25-codex-direct-read.md) | 2026-08-25 | Codex 接入模型 — 直接读 .claude/ 权威源（编译镜像退役）、同样执行开发任务、AGENTS.md 改入口指引 |
| [2026-08-25-gate-recipe-layering.md](2026-08-25-gate-recipe-layering.md) | 2026-08-25 | 按模式分层门禁链 — Quick 不再零门禁、g_knowledge 每链必含、dormant 语义、g_script NONE |
| [2026-08-25-feature-screen-debug.md](../../unity-developer/memory/2026-08-25-feature-screen-debug.md) | 2026-08-25 | RendererFeature 屏幕调试 — 真实 RenderGraph 中间 RT + Game View 观察，替代 Probe/Pixel Probe |
| [2026-08-25-no-screenshot-validation.md](2026-08-25-no-screenshot-validation.md) | 2026-08-25 | 移除活动流程中的截图验证提示，视觉结果改由 Editor 人工观察 |
| [2026-08-24-references-relocation.md](2026-08-24-references-relocation.md) | 2026-08-24 | references 归 agent — 顶层 references/ 迁入 unity-developer + rules 瘦身 + 合并 legacy 重复 |
| [2026-08-24-mcp-gate-audit.md](2026-08-24-mcp-gate-audit.md) | 2026-08-24 | .claude↔.mcp 门禁审计 — g_knowledge + settings deny + 顺序校验 |
| [2026-08-24-screenshot-removal.md](2026-08-24-screenshot-removal.md) | 2026-08-24 | 移除截图环节 — 验证统一为结构化 + 人工观测 |
| [2026-08-24-knowledge-internalization.md](2026-08-24-knowledge-internalization.md) | 2026-08-24 | 知识库内化 — MarkDowns → references/ + [G1.5] 硬门禁 |
| [2026-08-07-mcp-server.md](2026-08-07-mcp-server.md) | 2026-08-07 | Unity Gate MCP Server — 配方驱动门禁系统 |
| [2026-08-07-ecs-decoupling.md](2026-08-07-ecs-decoupling.md) | 2026-08-07 | .claude ECS 式三层解耦 |
| [2026-08-03-experiment-mode.md](2026-08-03-experiment-mode.md) | 2026-08-03 | Experiment Mode 加入 unity-developer |
| [2026-07-28-references-bootstrap.md](2026-07-28-references-bootstrap.md) | 2026-07-28 | Harness 参考库补全 |
| [2026-07-28-architecture-init.md](2026-07-28-architecture-init.md) | 2026-07-28 | .claude 7 层架构初始化 |

---

## 当前体系状态

- `unity-developer` ✅ — references, learnings, memory, cli, platforms, scripts
- `meta-developer` ✅ — references, memory

### 待办
- [x] **MCP live server 重连**（2026-09-21）：`delete_gated` 已落地并过测试；重启 stdio 进程**不够**（工具表按会话冻结），须 `/mcp` 重连——已由用户完成，item 5 两个文件已删。Bash 通道 `validation/delete_gated.py` 已补，此后不依赖重连
- [ ] **Codex 是否可用 Bash 删除通道**（2026-09-21）：`validation/delete_gated.py` 技术上也对 Codex 可见；是否授权属政策裁决，未擅自写进 `.codex/` 文档
- [ ] `settings.json` 的 `Write(/Assets/Mine/**)` 为死规则（从不被读取，仅触发启动告警），可清理——用户权限配置，未擅动
- [x] `.claude/skills/` 实体副本 → 逐 skill 相对软链（2026-09-04 裁决方案二，人工确认后执行；32 文件 → 9 软链）
- [ ] 删除已迁移旧路径的 compatibility stubs（agent/rules 软链为发现机制保留；仅剩 `.claude/agents/<role>/` 空目录壳物理清理，无 git 足迹）
- [x] standard Script/Shader 代码模板（2026-09-03 落盘：standard-script.cs / standard-shader.shader）
- [x] 模板核验补位（2026-09-20 完成）：`check_api_refs.py` 落 `.mcp/validation/`（词法 + 过时 API，
      `--compile` 可选深检），控制组三例全抓——其中一例抓出 CLI 自己「过时索引静默为空」的 bug。
      `urp-renderpass.cs`（3 处，含 2 个真实 API 错误）、`volume-template.cs`（过时 API）、
      `fullscreen-postprocess.shader`（emoji 标识符）已修，16 个模板全绿。
      判据与用法写进 `references/template-conventions.md` + `.mcp/README.md`；
      连带纠正 `rules/csharp-renderpass.md` + `volume-component.md` 写反的 VolumeComponent 规范。
      详见 [2026-09-20-parameter-panel-template.md](2026-09-20-parameter-panel-template.md)
