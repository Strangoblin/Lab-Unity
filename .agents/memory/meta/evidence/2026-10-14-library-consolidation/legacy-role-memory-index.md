# Meta Memory — .claude 体系变更记录

> meta-developer 的轻量变更日志。每次修改 .claude 体系后追加一行。

---

## 变更记录

| 日期 | 操作 | 涉及文件 | 原因 |
|------|------|---------|------|
| 2026-07-28 | 架构重构 | 全局 + 项目 .claude 全量 | 从 agent→skill→script 升级为 7 层闭环 |
| 2026-07-28 | 新建 | meta-developer.md + references/ + memory.md | 体系维护 agent 初始化 |
| 2026-07-28 | 新增 | unity-developer.md（会话收尾 + 自描述段） | agent 需要 session 收尾和 manifest |
| 2026-07-28 | 新建 | references/ (17 文件) | Harness 参考库补全 |
| 2026-09-03 | Reorganized | unity-developer/templates/script/ plus indexes/memory | Made Baker a Script template family member and reserved Generator/Manager/Controller siblings |
| 2026-09-03 | Verified | unityctl bridge host-isolation recovery + CLI note/memory | Made the shared-host bridge recovery procedure durable after reproducing and fixing Socket permission denied |
| 2026-07-28 | 废弃 | dirtybitgames-unity-editor, auto-manager/rules/ | 去重 + 内容迁移 |

---

## 当前体系状态

### Agent 层
- `unity-developer.md` ✅ — 自描述完整，会话收尾已添加
- `meta-developer.md` ✅ — references + memory 已初始化

### Reference 层（2026-08-24 归 agent 后，顶层 references/ 已移除）
- `agents/unity-developer/references/` ✅ — standard/（Script、Shader、Compute、Rendering）、shader/postprocess/、platform/（1）
- `agents/unity-developer/templates/` ✅ — 9 个模板（7 个代码模板 + 2 个文档模板）
- `agents/meta-developer/references/` ✅ (6 files)

### 待办
- [ ] 首次体系诊断（meta-developer Checklist M1-M7）
- [ ] 验证所有 cross-reference 无断链
