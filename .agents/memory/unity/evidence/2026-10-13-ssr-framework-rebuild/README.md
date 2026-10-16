# SSR 框架重构证据

- 所有者：unity-developer 的 `2026-10-13-ssr-framework-rebuild.md`。
- 用途：保存重构后的真实正/负控制、明确的临时 SSGI-only 同步计时与原配置恢复证据；不保存退役逐格/slab 源码。
- 依赖：Unity 6000.3.14f1 / URP 17.3 / Apple M2 Metal，当日 SampleScene、SSGI 设置及 timing-inputs.json 的生产 SHA256。功能 `Validation~/` 是可复跑源码，输出不依赖本目录；当前报告为版本历史，不保证新版本生成相同数字。
- 保留条件：作为该架构切换的历史基准；未来若不再需要，按 dated memory 和精确清单人工确认清理，不由 runner 覆盖。

## 内容

| 文件 | 用途 |
|---|---|
| library-results.json / library-distance-control.json | 固定采样/回退与资源合同、独立 GPU 距离负控制 |
| camera-results.json / camera-distance-control.json / camera-history-control.json | 真实相机、生产回归/档位/历史、距离及历史负控制 |
| *-cli.json | 当次 Unity CLI 原始响应；内含 tmp 路径是历史落点，对应报告已在本目录按名称保存 |
| timing.json / timing-inputs.json | 24 块 / 288 样本、isolated=true、源 Feature 状态与实际生产输入哈希 |
| Ray-Ray-*.png / DDA48-DDA64-*.png | 两尺寸临时 SSGI-only 最终显示；不是当前 Game View 性能或等画质证明 |
| checker-controls.json | 3 正控接受、18 缺失/错误控制拒绝；临时报告路径只是控制运行记录 |
| protected-before.sha1 / live-final.json | 六个保护文件未变、原 Renderer 内存 MaskRBR on / SSGI off、Edit Mode |
| library-accepted.log / camera-accepted.log / timing-accepted.log | runner 原始结果及 fixture 清理 |
| worker-review.md / worker-doc-audit.md | 隔离只读建议；最终结论由主 agent 复核，不把建议当 GPU 证据 |
| gate-report.log / gate-contracts.log / gate-contract-report.json | E5 review 提案与隔离契约通过、生产 state/audit 保持 |

JSON 内保留历史路径和输入原貌；复跑请从功能 Validation~ 生成新报告，不直接依赖历史 CLI 的 tmp 地址。PNG 的 IHDR 已确认 642×366 / 1920×1080，其中 642×366 两张已打开检查。
