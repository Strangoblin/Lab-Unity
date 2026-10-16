# Lab 存量产物核验

本报告没有删除或移动旧文件。精确逐文件路径、字节数、SHA256、Git 状态、引用与目标见 `inventory.json` 的 `records`。删除候选逐文件清单见 `delete-candidates.md`。

| 判定 | 路径数 | 现存文件数 | 字节 |
|---|---:|---:|---:|
| already_absent | 15 | 0 | 0 |
| keep | 75 | 75 | 742375 |
| keep_pending | 247 | 247 | 18261120 |
| proposed_archive | 71 | 71 | 51780139 |
| proposed_delete | 248 | 248 | 27689525 |

`proposed_delete` 是可供最终人工审批的候选；Hash 生成物须在归档 runner 与基线复跑通过后删除。`proposed_archive` 已准备逐文件映射与验证证据，先安装核验后退役原件。`keep_pending` 不属于本批删除。

- DDOF/SNN/SSL v1 7 文件及 SSL v2 2 文件在本任务之前已被 postprocess-port 任务删除，审计和 dated memory 可核验；SSC v1 2 文件仍在。
- TestProd 目录/meta 已缺失，审计未命中，保留来源未知的既有删除状态，不恢复，也不把它算成此次清理。
- shader-rename 84 项：60 当前逐字等于迁移结果、14 后续变化、10 已退役；没有仍等于 before 的项。但 9 个 before 与 4 个 after blob 未在 Git 历史中，已准备 Git blob + 13 唯一快照的精简归档；完整字段实际重建通过，原件待批准后退役。
- 默认 TutorialInfo/Readme 组的 GUID 外部引用检查见 JSON；存在组外引用时自动降为 keep_pending。
- Weather 临时文件只提议删除逐字节匹配正式源/Validation 的副本；活跃 localuv/before 保留；已结束阶段的独有规划/证据列入准备归档。
- 截图只提议删除 SHA256 相同的副本；不同编码与独有历史图保留。
- 正式 Weather Validation、Gradient TEST、TestAuto 与平台测试源码保留；依赖缓存、生产门禁状态/审计不作为垃圾。
- MaskBlur 六份 dense 数组已从冻结 CPU + 必要输入逐字重建，列入可重建输出删除；输入/参考工具/报告与旧 Git 回退 tar 已准备归档。活跃阶段基线和 FrameDebugger 唯一脚本保留。

## 分组

| 分组 | 路径 | 现存文件 | 字节 |
|---|---:|---:|---:|
| contract-test-fixture | 2 | 2 | 121 |
| documented-retired-config | 1 | 1 | 1653 |
| finder-metadata | 5 | 5 | 38932 |
| gradient-feature | 5 | 5 | 2992 |
| hash-archive-source | 5 | 5 | 336872 |
| hash-generated-harness | 25 | 25 | 1334686 |
| hash-isolated-project | 145 | 145 | 609094 |
| hash-stage-evidence | 7 | 7 | 99767 |
| hash-statistics-archive | 3 | 3 | 72479 |
| legacy-exact-staging-duplicate | 1 | 1 | 8641 |
| legacy-rebuilt-pbr-binary | 2 | 2 | 75424 |
| legacy-tmp | 105 | 105 | 53149800 |
| old-crash-report | 4 | 4 | 323418 |
| old-empty-testprod | 2 | 0 | 0 |
| old-ssc-backup | 2 | 2 | 14893 |
| platform-generated-output | 13 | 13 | 593006 |
| platform-test-source | 28 | 28 | 124759 |
| previous-meta-removal | 4 | 0 | 0 |
| previous-port-task-removal | 9 | 0 | 0 |
| production-state-audit | 4 | 4 | 136554 |
| root-old-run-log | 1 | 1 | 3714 |
| screenshot-byte-duplicate | 12 | 12 | 2079376 |
| screenshot-unique-or-referenced | 150 | 150 | 28364114 |
| shader-rename-unique-plan | 1 | 1 | 8939211 |
| snowy-plant-unique-history | 12 | 12 | 421164 |
| unique-recovery-scenes | 7 | 7 | 474449 |
| unity-default-tutorial | 14 | 14 | 52517 |
| unity-runtime-state | 2 | 2 | 50817 |
| weather-exact-staging-duplicate | 17 | 17 | 431165 |
| weather-unique-history | 35 | 35 | 308062 |
| weather-validation | 33 | 33 | 425479 |
