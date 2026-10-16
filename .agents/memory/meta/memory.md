# Meta Developer — Memory Index

> 按日期命名的 memory 文件索引。每次修改共享 agent 体系或平台适配层后追加。

---

## Memory 文件

| 文件 | 日期 | 摘要 |
|------|------|------|
| [2026-10-17-global-opencode-go-route.md](2026-10-17-global-opencode-go-route.md) | 2026-10-17 | 全局可达性：Codex / Claude 两侧各一份**逐字节相同**的薄路由 skill `opencode-go` + 三个明文路由入口（`~/.codex/AGENTS.md`、`~/.claude/CLAUDE.md`、`~/.claude/MEMORY.md`）；正文仍唯一留在仓库、不恢复副本；顺带修正 2026-10-15「已加路由」实际未落地（该文件为 0 字节）。strict 全绿、隔离 MCP 4/4、本机链路实跑 exit 0。 |
| [2026-10-16-project-adapters-back-to-project.md](2026-10-16-project-adapters-back-to-project.md) | 2026-10-16 | 用户裁决项目个体内容不得留在共享库：两处 `project-adapters/`（9 文件）迁回项目功能 `Assets/Mine/Scripts/ProductionTools/Validation~/` 与 `Assets/Mine/Special/HLSL/Validation~/hash/`，迁移后逐字节一致；技能改按 `validation.hash.*` 资源 ID 取用，`task-artifacts.md` 撤掉 project-adapters 规则；根目录 4 个 mono 转储建 checkpoint 后清理。strict 与四套隔离 MCP 全绿。 |
| [2026-10-16-local-remote-merge-windows-port.md](2026-10-16-local-remote-merge-windows-port.md) | 2026-10-16 | 合并远程 22 个提交（MCP v3 判定核 + 渲染/工具链）与本地 4 个提交（terrain 分层、CityScene、路径归一）；远程引入的 Windows 移植缺陷已修（`delete_gated` 缺 `O_NOFOLLOW`/`dir_fd` 退化、隔离 runner 只读 git 对象清理失败）；四套隔离 MCP 与架构 strict 全绿，Unity Editor 未验证。 |
| [2026-10-16-auto-manager-routing.md](2026-10-16-auto-manager-routing.md) | 2026-10-16 | Unity 首次任务分派收口到 auto-manager；SKILL 分类、AutoMode 一次启动、模式持有阶段，独立操作 / 报告分析轻路由，实施时升级；移除角色直连叶子，保留真实依赖。冷启动、架构、链接与隔离 MCP 验证通过。 |
| [2026-10-16-sustainable-development-startup.md](2026-10-16-sustainable-development-startup.md) | 2026-10-16 | 可持续开发判断归 auto-manager 单一正文，角色、入口与三模式在首次实施前接入；局部小修简化，已有结论复用，不增门禁或文档；新功能 / 小修冷启动演练、83 链接检查、架构与隔离 MCP 验证通过。 |
| [2026-10-16-production-tools-agent-templates.md](2026-10-16-production-tools-agent-templates.md) | 2026-10-16 | 用户裁决生产工具通用框架归 agent：完整独立窗口模板入 skill，五窗口独立 EditorWindow；12 文件迁出 Assets，原字节与 Git blob 留证，Lab 验证归适配器；编译、42 项 Editor 回归及独立模板验证通过。 |
| [2026-10-16-write-channel-rules.md](2026-10-16-write-channel-rules.md) | 2026-10-16 | **写入通道按域分**：`write_gated` 覆盖 `Assets/Mine` / roslyn / `tmp`，体系库（`.agents/**`）自 2026-09-10 起刻意不经 Unity MCP —— 失效的是规则文本（「唯一操作通道」+「hook 拦截 Bash 写入」实测只封三条红线），已改为显式两域 + `memory/README.md` 写入规则表；生产类工具框架入库 `templates/script/production-tool/`，`script.production-tools` 资源绑定；strict 与四套隔离 MCP 全绿、真实状态指纹未变。 |
| [2026-10-15-skill-content-audit.md](2026-10-15-skill-content-audit.md) | 2026-10-15 | codex-opencode-go 三份漂移归并为项目内单一权威（360行，git 留档），两个全局副本删除；移除 3 个冗余/失配技能目录；strict 与四套隔离 MCP 全绿。 |
| [2026-10-14-library-consolidation.md](2026-10-14-library-consolidation.md) | 2026-10-14 | 84软链清除、405文件归三库，六篇架构说明合一、角色21行；strict/隔离MCP全绿、346历史文件原字节。 |
| [2026-10-14-agent-skill-boundaries.md](2026-10-14-agent-skill-boundaries.md) | 2026-10-14 | Agent 索引/取用/记录、执行资源归 skill；51 项批准软链切换，资源位置与权限分离，strict/四套隔离 MCP 全绿。 |
| [2026-10-14-render-benchmark-skill.md](2026-10-14-render-benchmark-skill.md) | 2026-10-14 | 渲染同步墙钟相对A/B薄skill/角色协议、双端发现与入口同步；离线forward/default/隔离MCP通过，strict存量历史路径21项保留，不操作Unity或提取业务库。 |
| [2026-10-12-debug-layout-retirement.md](2026-10-12-debug-layout-retirement.md) | 2026-10-12 | 专用 InteriorMapping 全屏验证退役后更新布局当前状态；2026-10-10 迁移表与 dated memory 保持历史语义，通用图像验证流程位置不变。 |
| [2026-10-10-debug-layout-executed.md](2026-10-10-debug-layout-executed.md) | 2026-10-10 | 用户确认后执行 Special/Debug 集中布局；共享 skill / Roslyn CLI 活动入口同步，旧来源表保留供回退；GUID、Unity 编译与完整 PNG 证据统一由 Unity memory 记录。 |
| [2026-10-10-gate-directory-deletion.md](2026-10-10-gate-directory-deletion.md) | 2026-10-10 | `delete_gated` 补齐目录：空目录默认 / 非空须 `recursive` 且子项逐个判定 / 软链与作用域根拒绝 / 预检后变化零改动；三通道同源，23+30+5 隔离契约与架构 strict 全绿；顺带修 macOS grep `-Z` 导致的 GUID 命中合并 |
| [2026-10-10-task-artifacts-lifecycle.md](2026-10-10-task-artifacts-lifecycle.md) | 2026-10-10 | 会话产物规范与隔离测试验证完成；批准清单执行：248 文件清理、71 旧源归档、113 项安装、4 文档更新，正式入口复跑与 Unity 刷新通过。 |
| [2026-10-10-unity-render-debug-skill.md](2026-10-10-unity-render-debug-skill.md) | 2026-10-10 | 全屏 Shader / Feature Debug 与 SingleCameraRequest 完整 PNG 合并为共享 skill；取像代码独立、两端软链发现、旧入口去重。离线编译 / 架构 / 隔离 MCP 检查通过；Special/Debug 迁移仅列候选未执行。 |
| [2026-10-10-behavioral-reuse-criteria.md](2026-10-10-behavioral-reuse-criteria.md) | 2026-10-10 | 复用按最终作用与范围裁决；常数/结构不同不自动保留，接受类似随机效果时可统一序列；核验分布及空间/时间/通道相关性。 |
| [2026-10-10-feature-reuse-workflow.md](2026-10-10-feature-reuse-workflow.md) | 2026-10-10 | 同类功能关联 skill：从候选实源比较公共算子与流程、默认保留行为；区分项目公共/通用公共，只有后者可入库；公共路由接入与两平台发现入口。 |
| [2026-10-09-mcp-content-audit.md](2026-10-09-mcp-content-audit.md) | 2026-10-09 | **MCP 内容审计**：修 9 处（重复 5 / 过期 7）——`mcp-gate-usage.md` 154→81 行回归单一角色、E5 **退出码写反**、7 处「v2」提法、`knowledge.md` 的「强制阻断」、`project_paths` 注释不准、`script_library` 4 条死数据；新增 `platform_hook_drift` 检查（两平台 guard-bash 逐字一致，**带控制组实测**）。**4 组删除已执行**（5 死门禁模块 / 咨询层 72 行死代码 / tmp/.reusable 3 旧脚本（走门禁删除通道留审计）/ .mcp/tmp 残留）。**归属修正**：`check_api_refs.py` 迁出 `.mcp/validation/` → `meta-developer/scripts/`（`.mcp/README.md` 整节移除，4 处引用同步） |
| [2026-10-09-policy-demotion-executed.md](2026-10-09-policy-demotion-executed.md) | 2026-10-09 | **首批策略降级执行**（E5 首次实跑，只改数据未动代码）：`ceremony.recipe_gate` + `ceremony.knowledge_high_priority` → warn（整条配方链变建议，观察一个任务周期）；**驳回**「零命中即退役」的机械提案（`norm.*` 拦的是产物不可用，零命中是健康 → 标 `retirable:false`）；**测试改为断言机制而非 policy 取值**（套件钉 strict + `test_shipped_policy` 防漂移）。顺带修掉 `friction_report` 的**双模块实例**导入 bug 与两个报告缺陷 |
| [2026-10-09-workspace-boundary.md](2026-10-09-workspace-boundary.md) | 2026-10-09 | **任务级工作目录边界**：`gate_set_workspace` 收窄基础作用域（只能收窄不能放宽），`Assets/Mine/**` 的写/删/移全受约束、`tmp/` 豁免；**「agent 提出、人工批准」做成真分权** —— 登记（`gate_request_workspace`，不授权）↔ 批准（CLI）↔ 封闭（hook C3 拦 agent 自批），`workspace_view()` 合并磁盘使批准对运行中的 server 立即生效。顺带修掉「测试写生产摩擦账本」的噪声源 |
| [2026-10-08-mcp-v3-self-observing.md](2026-10-08-mcp-v3-self-observing.md) | 2026-10-08 | **MCP v3**：判定核唯一化（`validation/judgement.py`，server 与 CLI 退为薄壳）+ 策略数据化（`policy.json`，缺省 = v2 行为，只放宽不收紧）+ 摩擦账本（`friction.jsonl` → `friction_report.py` 提案）。补齐 **write 的 CLI 通道**（三条操作在两个传输上等价，Codex 不再是二等）。元问题答：门禁是控制器不是过滤器 —— sensor→报告→policy 数据 + 能力契约 + E5 收尾程序。证据取自会话库（902 调用 = 542 真实 + 342 仪式 + 44 DENIED） |
| [2026-10-08-opencode-go-model-v41-flash.md](2026-10-08-opencode-go-model-v41-flash.md) | 2026-10-08 | opencode-go 换 `deepseek-v4.1-flash`（37 模型、直连可用）：**catalog 是模型元数据与面板列表的唯一来源**，换到 catalog 外的模型会静默用 fallback 元数据（窗口失真→压缩时机变），补条目 key 是 `slug`；上游 2026-10 新增 `x-opencode-session` 头使旧裸 curl 验证静默失效（codex 自身不发也通）；0.159.2 新忽略 `disable_response_storage` 等 2 个 key |
| [2026-10-08-codex-transport-toggle.md](2026-10-08-codex-transport-toggle.md) | 2026-10-08 | codex 传输轴 `cx-http` / `cx-ws` 落地：`[model_providers.openai]` 保留 ID 在 0.159.2 仍被拒，唯一解是**改名 provider 指回官方后端** + `requires_openai_auth`（保住 OAuth，实测打到官方后端返回业务错误而非 401）+ `supports_websockets=false`（零重连，对照 WS 路径 5 次 `Reconnecting...`）；**推翻** skill 里「改名 provider 丢 OAuth」的未实测推断。托管块须插在首个 `[table]` 前（TOML 裸键归属表头）、校验只能用 `codex features list` |
| [2026-10-05-gate-path-windows-regression.md](2026-10-05-gate-path-windows-regression.md) | 2026-10-05 | 修复 `validate_path` 在 Windows 上的分隔符比较回归（35a2fd6 引入）：`Assets/Mine/**` 三通道被误判 `PATH_NOT_ALLOWED`；比较两侧归一化 + 跨盘按拒绝、补单元与端到端回归用例、`test_knowledge_paths` 首个断言改 Path 相等（曾掩蔽同文件内的 Assets/Mine 用例）。附 Codex 工具可见性 vs 政策声明的待裁决项。 |
| [2026-10-05-local-remote-merge.md](2026-10-05-local-remote-merge.md) | 2026-10-05 | 合并远程体系与本地 OpenCode skill、Claude 发现软链；strict 通过，记录 Windows MCP 测试限制。2026-10-16 迁移后软链方案由零软链架构取代，skill 与本文记录保留。 |
| [2026-09-21-gate-operation-model.md](2026-09-21-gate-operation-model.md) | 2026-09-21 | 门禁从「按文件」改「按操作」建模：新增 `move_gated` 补**复合面**（移动 = 写新 + 删旧，单独看每步判定都对、合起来误拦 → 验净效果）；批次级检查须排在逐文件检查前、链式判定须对称（两处都是测试跑出来的）；落点校验从**休眠**接上线——**只收受管扩展名 + 顶层根只到一层**，照现实布局写（202 个受管文件 0 误报），新文件 error / 已有 warning；`category` 注解升为可校验。附审计结论「门禁不知道自己的对偶操作」与三处未做项（写入/移动无 CLI、`g_file` 遗留、`cp` 读出被拦属已裁决取舍） |
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
| [2026-08-25-feature-screen-debug.md](../unity/2026-08-25-feature-screen-debug.md) | 2026-08-25 | RendererFeature 屏幕调试 — 真实 RenderGraph 中间 RT + Game View 观察，替代 Probe/Pixel Probe |
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

- 入口：平台 → agents清单 → 所需 memory / knowledge / skill，全部明文，无软链或正文镜像。
- agents只存两份角色定义；三库实体独立。完整当前事实由knowledge/meta维护，旧迁移状态留dated记录。

### 待办
- [ ] 2026-10-14 E5：复核 `norm.region-added` / `placement.root.new` 的零命中提案，未改策略。
- [x] **MCP live server 重连**（2026-09-21）：`delete_gated` 已落地并过测试；重启 stdio 进程**不够**（工具表按会话冻结），须 `/mcp` 重连——已由用户完成，item 5 两个文件已删。Bash 通道 `validation/delete_gated.py` 已补，此后不依赖重连
- [x] **Codex 是否可用 Bash 删除通道**（2026-10-08 裁决并落地）：三通道统一为 `gate_cli.py {write,delete,move}`（同一判定核），已明确写进 `.codex/AGENTS.md` + `INTERFACE.md`；`write` 通道补齐（此前是三条操作里唯一单通道的）
- [x] **工作目录边界**（2026-10-09）：`workspace.required=true` 已生效（用户要求任务开始先确认目录），流程已在 skill 加 `[G0.5]`
- [x] **首条策略降级已执行**（2026-10-09，人工批准）：两项 → `warn`；防幻觉与产物可用性类保持 `block`。**观察窗口进行中**（一个完整任务周期）—— 到期看 `friction.jsonl` 的 `warn.ceremony.recipe_gate` 频率与产物质量，决定维持或升回 `block`
- [ ] `settings.json` 的 `Write(/Assets/Mine/**)` 为死规则（从不被读取，仅触发启动告警），可清理——用户权限配置，未擅动
- [x] `.claude/skills/` 实体副本 → 逐 skill 相对软链（2026-09-04 裁决方案二，人工确认后执行；32 文件 → 9 软链）
- [x] 清除架构软链与旧发现入口（2026-10-14用户明确要求）；改明文路由，不再保留兼容软链。
- [x] standard Script/Shader 代码模板（2026-09-03 落盘：standard-script.cs / standard-shader.shader）
- [x] 模板核验补位（2026-09-20）：`check_api_refs.py` 落 `.mcp/validation/`（词法 + 过时 API，`--compile` 可选深检），控制组三例全抓（含 CLI「过时索引静默为空」）；`urp-renderpass.cs`（2 个真实 API 错误）、`volume-template.cs`、`fullscreen-postprocess.shader` 已修，16 个模板全绿；判据进 `references/template-conventions.md` + `.mcp/README.md`，连带纠正写反的 VolumeComponent 规范 —— 详见 [2026-09-20-parameter-panel-template.md](2026-09-20-parameter-panel-template.md)
