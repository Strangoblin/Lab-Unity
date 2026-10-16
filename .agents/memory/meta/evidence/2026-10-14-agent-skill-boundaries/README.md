# Agent / Skill 职责整理证据

所有者：meta-developer；用途：复核 2026-10-14 经用户确认的 51 文件技能归属切换和职责 / 路径解耦验证。

- [migration.json](migration.json)：精确源 / 目标、切换前 SHA-256 / Git 状态、兼容软链及目标签名。
- [validation.json](validation.json)：strict、控制组、隔离 MCP、模板核验与真实验证范围摘要。

这是历史审计证据，不是活动资源配置。具体结果见 [会话记录](../../2026-10-14-agent-skill-boundaries.md)。测试 fixture、全量源码工作树及可重建日志不归档。

依赖：共享规范、skill 权威资源与登记别名；原测试输出在关联 tmp/sessions 目录，可按工具协议重建。保留至旧兼容入口完成退役并另有可追溯审计替代；清理前仍按项目清单确认规范处理。
