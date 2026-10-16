# 模板 API 核验

> [template-conventions.md](../../../knowledge/meta/template-conventions.md) 留事实约定；本文件承载模板 API 核验的能力、控制组要求与边界。不改脚本源码，不调用 Editor，不涉及 Assets 业务代码。

## 目标

要的不是「能编译」，是「写下的 API 真实存在且没过时」。模板是给人**拷贝**的参考实现，不要求自足编译（骨架含占位符，`.shader`/`.hlsl` 更没法用编译器验）；但任意一行错 API 会复制进每一份拷贝。核验无完整性前提，`.cs` / `.shader` / `.hlsl` / `.compute` 一视同仁。

## 脚本

所属脚本 `check_api_refs.py`（用途、源码与入口一致；索引见 [scripts/README.md](../scripts/README.md)）。

```
python3 .agents/skills/agent-maintenance/scripts/check_api_refs.py <dir>
python3 .agents/skills/agent-maintenance/scripts/check_api_refs.py --compile <自足骨架.cs>   # 可选深检
```

| 检查 | 抓什么 | 覆盖 |
|------|--------|------|
| 词法 | 标识符内非 ASCII（注释 / 字符串之外） | 四种 |
| 过时 | 引用的名字在包源码里带 `[Obsolete(..., true)]` | 四种 |
| 编译（`--compile`） | 凭空 API 及其余一切 | 仅自足 `.cs`，需 dotnet |

## 控制组要求

- **必须带控制组**：把已知缺陷注入副本，确认能被抓到。首次实现时正是控制组发现「过时索引静默为空、检查 2 一直在空转」。
  **一个静默空转的检查比没有检查更坏** —— CLI 已内置索引为空时告警。
- **一次刷全族**，别只测刚改的：存量模板同样可能从未验过。

## 边界（不假装）

凭空造出的成员名只有编译器能判——过时检查只能判「存在但已废弃」。`.shader` / `.hlsl` 的真编译依赖 Unity 导入，本层不做。

## 与规范的关系

模板编写规则（横幅 / 占位符 / 族目录 / Metal 兼容等）见 [template-conventions.md](../../../knowledge/meta/template-conventions.md)。本文件只覆盖 API 能力与验证流程。
