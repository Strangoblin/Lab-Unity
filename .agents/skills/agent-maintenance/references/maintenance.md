# 体系维护流程

1. 从 `.agents/agents/README.md` 选角色，直接读取本次涉及的 memory / knowledge / skill；不扫描整个共享库。
2. 按 [架构约定](../../../knowledge/meta/architecture.md) 判断归属。角色读取“范围、取用、记录”三段，不要求重复的自描述 / manifest。
3. 查目标库已有索引和同主题内容，再编辑唯一实体位置；步骤和实现归 skill，事实归 knowledge，结论归 memory。角色模板见 [asset](../assets/agent-template.md)。
4. 改位置时记录精确源 / 目标和引用，核对冲突与源字节；依据本任务已有授权执行存量移动 / 删除。没有明确授权时按根 AGENTS.md 确认。旧别名全部移除，不创建软链、正文镜像或逐文件跳转壳。
5. 更新活动索引、调用方和解析器；历史 dated memory / evidence / 基线原字节保留。入口只写取用线索，不为每个索引添加反向引用或层层 README 必读。
6. 按变更运行下面检查，输出真实覆盖和限制，再交角色回写同领域记忆。测试产物按 [合同](../../../knowledge/meta/interfaces/task-artifacts.md) 归属。

| 验证 | 命令 / 入口 |
|---|---|
| 架构、活动断链、零软链、角色归属 | `python3 .codex/tests/06_architecture/verify.py --strict` |
| 执行契约 | `python3 .mcp/tests/run_isolated.py`（隔离项目，不 reset 生产） |
| 路径残留 | `rg -n "旧路径" <本次活动文档范围>`，历史来源另按迁移记录解释 |
| 模板 API | [template-validation](template-validation.md) |

## E5：门禁演进

涉及门禁判定 / 路径时读取 `python3 .mcp/validation/gate_cli.py report`；review 提案形成具体待办。
策略调整优先改 policy 数据，须人工确认并记录依据、观察窗口；判定改动改唯一核，经隔离契约验证。不能以本次迁移为由放宽权限，生产状态与审计不作为 fixture 清理。
