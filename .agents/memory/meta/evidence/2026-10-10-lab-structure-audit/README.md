# Lab 清理与会话产物收尾（2026-10-10）

用户以“无误，可进行”批准 [逐文件清单](cleanup-plan.md)。该清单保留原始准备状态；实际执行结果以 [execution-report.json](execution-report.json) 为准。

- 248 个无用/可重建文件清理（27,689,525 B）；71 个旧源在归档核验后移除（51,780,139 B）。
- 113 项正式归档安装（80,331,162 B，含必要外部输入的复制）；4 个批准文档更新完成。
- 322 个保留项已复核：318 个文件与清单哈希相同，4 个生产 state/审计文件保留并按门禁操作正常更新；15 个此前已缺失路径不计本次删除。
- 清理 78 个空目录，保留 14 个非空目录；SSC 的空备份目录当时因删除通道只支持文件而保留，同日补齐目录删除能力后已清掉。
- Hash 当前合同与统计、dense 不可变 CPU 参考均从正式路径实际复跑；Unity 资源刷新显示 compilation succeeded。

## 可发现入口与证据

- [Hash 正式脚本入口](../../../../unity-developer/scripts/validation/hash/README.md)、[Unity 历史资料索引](../../../../unity-developer/memory/2026-10-10-validation-artifact-archive.md)。
- [Hash 安装后检查](evidence/installed-hash-check.json)、[dense 安装后检查](evidence/installed-dense-check.json)；Hash 后续整文件历史漂移单列，CPU/SPIR-V 结果不证明 GPU/画面等价。
- [测试运行器验证](evidence/runner-verification.md)、[故障控制](evidence/hash-archive-control-checks.json)。运行器验证是执行清理前的独立测试快照，不代表当前生产 state 哈希未变。
- [完整逐文件盘点](inventory/inventory.json)、[精确安装计划](cleanup-plan.json)、[旧源映射](legacy-source-map.json)。用途未知和服务于未提交工作的材料保留原位。
- [会话收尾清单](task-closure.json) 记录本任务临时文件哈希、所选证据迁移和最终验证；运行生成物不归档。

本目录归 meta-developer，只保存清单、结论与必要验证证据。外部原始回读只复制，未删除；未 reset/stash/add/commit 脏工作区。任务目录收尾仅保留指向本目录的 README。
