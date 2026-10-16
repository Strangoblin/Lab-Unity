---
name: Shader 菜单路径规范内化
description: 固化 Mine Shader 命名约定并同步共享模板
date: 2026-09-17
---

# Shader 菜单路径规范内化

- 将本次 Mine Shader 整理约定写入 [shader-development.md](../../../rules/shader-development.md)，作为唯一命名规则来源。
- Shader reference 索引增加规则链接；同步普通、Render、PostProcess Shader 及 Baker 查找模板，避免继续生成旧前缀。
- 规则覆盖 Shader Graph 路径；保留内置 Shader 引用与资源 GUID，区分菜单路径和磁盘路径。
- P1-P3：复用现有规则文件，仅在索引链接；不复制规范正文，不调整业务代码或门禁实现。
- Claude/Codex 通过现有共享规则软链读取同一文件，无需新增平台副本。
