# Lab 清理与归档审批清单

状态：准备完成，尚未批准、尚未执行本批存量删除/移动。规范与测试基础设施已修改并验证。

精确动作由 [cleanup-plan.json](cleanup-plan.json) 锁定；每个文件的用途、引用、Git 状态、SHA256 和目标见 [inventory.json](inventory/inventory.json)。执行前重新核对哈希，发生变化的内容不覆盖。

## 本批动作

| 动作 | 现存旧文件 | 原大小 |
|---|---:|---:|
| 删除可重建/已核实无用产物 | 248 | 27.69 MB |
| 安装并核验归档后退役旧位置 | 71 | 51.78 MB |
| 保留当前源码、测试、状态等 | 75 | 0.74 MB |
| 保留活跃基线/用途未明历史资料 | 247 | 18.26 MB |

另有 15 个此前已缺失路径，单列追溯，不计作本批删除。

- [逐文件删除表](inventory/delete-candidates.md)：旧 crash、DS_Store、root run.log、默认 TutorialInfo/Readme、SSC 旧备份；隔离项目/编译物/平台输出；17 个 Weather 正式源逐字副本、12 个截图逐字副本等。六份 dense NPY 在依赖和参考工具归档后按复现证明删除。
- [逐文件归档表](inventory/archive-candidates.md)：Hash 工具及不可变输入、Shader 菜单改名历史、Weather 整合/Snowy/Plant 历史、AO 图与原回退包、MaskBlur 参考工具/必要输入/报告。
- 归档安装 113 个文件，约 80.33 MB。包含本次新增说明/证明和 10 份必需外部 RGBA 输入（36.86 MB，仅复制）；不把外部源纳入删除。不以旧文件退役字节数宣称净磁盘收益。
- 旧 Shader rename 8.94 MB 原计划用 155 个可回读 Git blob 与 13 份唯一正文保留完整字段；完整重建核验通过。不会丢弃未入 Git 的内容。
- 旧 tar 是既有唯一回退资料，随证据归档；不会生成新备份 tar/zip。历史脚本不作为当前功能入口。

## 归属与收尾

Hash 可复跑工具、必要 fixtures 和 README 进入 `.agents/agents/unity-developer/scripts/validation/hash/`。
历史证据进入该角色 `memory/evidence/<日期-主题>/`，按六个独立主题记录背景与依赖；规范由 `.agents/interfaces/task-artifacts.md` 统一路由。

安装后应用 [4 项文档变更](pending-edits.json)：登记 Hash 脚本入口、新建并索引 Unity 归档记录、修正 AO.md 失效采集脚本引用。AO 文档写入走 Unity 门禁；原读数标明是历史结果。

JSON 还列出 93 个精确目录候选，仅在为空时用 rmdir 清除。不会递归删除未知文件。
最终把本审批清单、精确 inventory/来源映射和验证结论保留到 Meta dated memory 的 evidence；归档成功后清掉本轮可重建 output/fixture 与重复 staging。

## 保留边界

Weather Validation、TestAuto 契约 fixture、Gradient TEST、正式平台测试保留。天气 localuv、MaskBlur 阶段基线、FrameDebugger 唯一源码、无充分上下文的旧截图和独有 Recovery 场景保留；不会凭 test 名称或缺少场景引用删除。

Library/Temp/虚拟环境/IDE 工程及生产 `.mcp` state/审计、`.unityctl` 活跃状态保留。当前已有脏改动不自动 stash/add/commit。

## 已完成的核验

- 架构 strict、8 个扫描控制通过；MCP 隔离 runner 9/9、30+5 契约及 registry 自检通过，生产 state/审计哈希一致；Bash runner 副本控制 10/10。
- Hash 当前合同重跑：63,212 CPU 比较零差异、范围/HLSL/SPIR-V/故障控制通过；四个后续源修改的历史整文件漂移明确单列。
- dense NPY 六份全部从冻结 CPU 工具与必要输入重生成，SHA/shape/dtype 与固定预期相同，报告数值一致；归档不再需要保存生成数组。
- [只读执行前核验](evidence/cleanup-preflight.json)：319 个旧动作源、113 个准备安装项、4 项文档前置条件通过。安装后会再验证正式路径入口。

这些结果不包含 Unity/Metal/GPU 实测。已核对所有存量 tmp、旧 Codex tmp 和截图；无法证明无用的项目保留，不假称已全部清除。

## 确认范围

批准即执行本 JSON 的精确删除、归档、空目录清除及 4 项文档登记；已有授权不重复确认同一清单。
