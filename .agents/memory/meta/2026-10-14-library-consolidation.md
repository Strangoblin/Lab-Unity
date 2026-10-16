---
name: library-consolidation
description: 移除84架构软链，405文件归独立memory/knowledge，入口只经agents清单；六篇架构说明合一，历史346文件原字节验证。
date: 2026-10-14
---

# 三库收敛与明文路由

用户要求继续精炼：各平台入口先读 agents 清单，然后直接到 memory / knowledge / skills；清除全部软链。又明确创建独立 memory，knowledge / skills 沿用，其余内容收敛三库。

## 实际结果

- 共享核心只有 agents / memory / knowledge / skills。两份角色仅21行，各自“范围、取用、记录”，不追加重复 manifest；agents 清单直达领域记忆、知识和匹配技能。
- 84 项架构软链已按本轮明确指令移除，包括上一阶段51资源别名、Claude27入口及Codex6目录别名。各平台根入口和agents README只有明文；两 thin TOML 是必要运行时配置，不是正文镜像。依赖环境与故障注入 fixture 软链不在清理范围。
- 角色记忆→memory/meta、memory/unity；事实→knowledge/meta、knowledge/unity；规则→knowledge/rules；接口→knowledge/meta/interfaces。404项主迁移加旧meta轻量日志1项归档；Unity知识根README同名碰撞合为唯一索引，不增加references层。
- 六篇角色格式、层级、引用、职责、拓扑说明→knowledge/meta/architecture.md。接口只留必要合同；平台接入手册停止镜像，维护技能与模板按三点格式更新。外部来源总表位于knowledge/external，不新增外部agent，不搬第三方技能实体。
- 同步活动链接、工具知识根、全局Unity路由/记忆索引/偏好；稳定unity知识ID保持。旧脚本授权移除，Editor canonical前缀保留，camera只转移原单文件权利，不授权父目录或邻文件；安全策略与hook未放宽。
- 独立请求review还定位原技能重复gate_set_recipe会清空入口记录的执行漏洞；已改为初始化一次，G1和迭代不重置，新增真实RPC正路径与重复初始化负控制。两平台hook只改旧知识位置注释，执行逻辑保持。
- 各入口不强制层层README读取，知识保存事实、技能保存执行、记忆保存上下文；旧dated记录和证据原字节保留。历史相对链接按原语境和迁移映射解释，不保证直接点击旧链接有效，不恢复软链。

## 验证

- strict全绿：活动断链、架构软链、角色资源违规、平台漂移、绝对路径与入口错误均0。
- 路由/职责20、扫描8、隔离runner9控制通过；MCP四套件含新初始化回归隔离通过，测试窗口生产state/audit指纹不变。隔离输入改为合成Shader，不读取生产Assets。
- 346个dated记忆/证据（包含内部README和旧轻量日志）SHA逐项一致；活动索引按当前结构更新，不改历史。
- agent-maintenance / codex-bridge的skill校验通过；外部清单及README由OpenCode flash一次限定5文件完成，22个引用通过，主会话复核。
- 只处理体系，不读取或修改生产Assets，不运行Editor。当前工作树还有此前用户变更；原始活动文本、迁移/合并SHA与代码diff保留规范会话staging，未自动提交混合脏改动或stash。

## E5 与后续

报告观察到510次真实操作、仪式比0.11，region-added / placement.root.new仍为零命中复核提案；本阶段未调策略，只继承待办。
后续精炼优先压缩活跃上下文，历史按日期检索；知识正文固定结论/适用条件/来源，减少讲述迁移史；外部仅登记来源/版本/本地偏离，不复制维护第二份上游手册。

精确证据见 [归档](evidence/2026-10-14-library-consolidation/README.md)。可重建输出与源语境快照在 tmp/sessions/2026-10-14-agent-libraries-consolidation，不把完整临时工作区归档。
