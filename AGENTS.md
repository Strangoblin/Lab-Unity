# Unity Lab — 项目入口

Unity 6 / URP 17+ 渲染实验室。先读 [Agent 清单](.agents/agents/README.md)，再直接取用匹配角色的 memory、knowledge 或 skill；不预加载全部库。

共享内容在 `.agents/{agents,memory,knowledge,skills}`，入口均为明文，不建软链或镜像。平台配置归 `.claude/.codex`，实际门禁与项目资源绑定归 `.mcp`。

安全边界：存量删除 / 移动列精确清单并取得人工确认；同一授权不重复请求。禁止 `git stash --all`，保护已有改动。业务资产修改须任务授权及现有门禁；本体系整理不扩展资产权限。
文档和配置用仓库相对路径；每个架构阶段独立提交并保留可回退点，不把用户脏改动混入自动提交。
