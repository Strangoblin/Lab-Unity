---
name: safety-lessons
description: 历史踩坑经验 — 安全红线 + 清理规则 + 备份策略
date: 2025-06-15
metadata:
  type: project
---

# 安全经验教训

## 红线（已在 C1-C7 中体现）

- C1: `git stash --all` 永久禁止
- C2: 不碰 `Assets/Mine/` 下功能代码
- C3: 删除操作先列清单再确认

## 经验教训

| 教训 | 说明 |
|------|------|
| **git stash --all 危险** | 会删除所有未跟踪文件，恢复可能因冲突失败 |
| **备份必须精确** | `git stash push -- tmp/ Screenshots/` 只备份临时文件 |
| **场景物体用脚本** | 不直接 rm，用 `unityctl script execute` + `DestroyImmediate` |
| **重清理不删设计文档** | `Plan.md` 是项目资产，不删除 |
| **可复用脚本存 .reusable/** | 通用脚本放入 `tmp/.reusable/`，清理时跳过 |
| **测试物体按框架下挂** | 同框架测试物体挂到对应父容器下 |
| **渐进式清理** | 先轻后重，轻清理可随时执行 |
