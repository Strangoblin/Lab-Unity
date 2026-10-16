# 验证资料与历史产物归档

本轮按逐文件批准清单整理历史产物；共享规范见 [task-artifacts.md](../../../interfaces/task-artifacts.md)。正式 Weather Validation、仍服务于未提交工作的基线及用途未明的旧图保留原位。

- [Hash 当前验证与历史边界](../scripts/validation/hash/README.md)：带不可变输入；当前 CPU/范围/HLSL/fault 合同通过，四个后续修改源的历史整文件漂移单列；无 Unity/Metal/GPU 验证。
- [Shader 菜单改名](evidence/2026-09-17-shader-rename/README.md)：Git blob 加 13 个唯一快照保存完整计划字段；复核后退役旧整包计划。
- [Weather 整合](evidence/2026-10-09-weather-integration/README.md)、[Snowy 原型](evidence/2026-09-07-snowy-prototype/README.md)、[Plant 规划](evidence/2026-09-17-plant-billboard/README.md)：历史文本与必要证据；不能作为当前代码或工具导入。
- [AO 历史读数](evidence/2026-09-21-diffusegi-ao-cleanup/README.md)：保留旧回退包、选定图与原采集脚本；AO 正式说明指向历史来源，当前实现需重新采集。
- [MaskBlur 暗点参考](evidence/2026-10-09-maskblur-darkspot/README.md)：冻结 CPU 参考及必要线性 RGBA 输入，原始数据仅复制；复跑数值一致，不代表当前 Shader GPU 结果。

精确存量动作、源/准备/目标 SHA256、保留项和实际执行状态见 Meta 的 [审计记录](../../meta-developer/memory/2026-10-10-task-artifacts-lifecycle.md)。不新建回退压缩包，不改历史 dated memory。
