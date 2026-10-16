# Claude 项目入口

先读 [Agent 清单](../.agents/agents/README.md)，按任务直接取用对应角色和三类库。共享正文只在 `.agents/` 编辑。

本层只维护 settings / hooks / MCP 注册。agent、skill、paths 规则不再由平台软链自动注入，使用清单明文按需读取；具体操作由匹配 skill 承载。现有权限与 MCP 门禁仍执行。
