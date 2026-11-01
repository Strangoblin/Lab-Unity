# 历史日志格式

每个领域一份：`.agents/memory/meta/history.md`、`.agents/memory/unity/history.md`。
**逐日一行**，不是逐条。它替代被退役的 dated 记录，只回答「那天干了什么、结论去哪了」。

```markdown
# Meta — 已归档记忆

> 被结构自检退役的记录在此逐日留一句。正文已删；完整理由与验证见 git 与 `deletes.jsonl`。

| 日期 | 当天做了什么 | 归档记录 | 去向 |
|------|-------------|---------|------|
| 2026-08-06 | POSS 逐物体软阴影落地（Atlas + 屏幕空间 PCF，投影 10m 硬编码）；同日修 SSR DDA 鱼眼 w 翻转 | poss-per-object-soft-shadow, ssr-dda-fisheye-w-sign-flip | `../../knowledge/unity/shader/postprocess/shadow.md` |
| 2026-10-28 | 垃圾桶清空：v5 隔离测试中间产物 4 桶，共 412 文件 | trash:152610-7569b2 等 4 桶 | 内容不留，`deletes.jsonl` 可查 |
```

> 示例里的去向用反引号是为了让模板自身不带死链；**真实条目写成指向目标文件的 markdown 链接**，
> 路径相对所在领域的 `history.md`。


## 规则

| 项 | 规则 |
|---|---|
| 粒度 | 一天一行；同日多条合并，`归档记录` 列列出 slug |
| 动作列 | 写**结果**，不写过程。不超过 60 字 |
| 去向 | 可点的相对路径；内容不留的写清楚留痕在哪 |
| 顺序 | 新行加在表尾，不重排。已归档的桶不再改变 |
| 内容 | 不复述结论本身 —— 结论在 knowledge / skill，这里只留指针 |
