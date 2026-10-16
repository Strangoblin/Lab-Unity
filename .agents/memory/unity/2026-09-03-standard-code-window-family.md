---
name: standard-code-window-family
description: 2026-09-03 模板体系第三轮:standard 补代码框架 + renderpass 双变体合并 + postprocess 文档并入 README + window 家族新开。
metadata:
  type: architecture
---

# Standard 代码框架 + 独特模板去重 + Window 家族 — 2026-09-03

## Context

模板重分类(见 [[template-reference-taxonomy]])后遗留三缺口:standard/ 无代码文件、独特区存在重复与越界 md、editor-baker-window.cs 悬空(见 [[editor-window-family-ruling]])。用户裁定四项,本轮全部落地。

## 裁定与落地

1. **standard/ 层带代码**(用户选「通用代码骨架」方案):
   - 新增 `templates/standard/shader/standard-shader.shader` — 普通 URP 材质 Shader 可编译骨架(Properties → HLSLINCLUDE → 纯 Pass SubShader;对齐 `references/standard/shader/shader-structure.md` + PBRToon 实源)
   - 新增 `templates/standard/script/standard-script.cs` — 双形态可编译骨架(形态 A 静态工具类 / 形态 B MonoBehaviour,复制后二选一)
   - standard 现 = 代码骨架 + 文档模板并列;`references/standard/` 仍是规范文本 → 「规定/骨架」配对模式
2. **renderpass 双变体合并**(taxonomy 的 "pending explicit deletion" 获批准):`urp-renderpass.cs` 保留为唯一 RenderPass 模板,`render-pass-template.cs` 删除;postprocess README 表删行
3. **postprocess 文档并入 README**:`script-doc-template.md`(套壳 9 节 super-set,零外部引用)删除,文档写作要点压缩为 postprocess/README.md 一节 → 独特模板区(script/shader 各族)实现「只有 README.md 是 markdown,其余全是代码模板体」
4. **window 家族新开**(editor-baker-window.cs 缺失触发,用户裁定归属):`templates/script/window/` = README(消费差异表)+ editor-window.cs(通用壳 `YourToolWindow`);baker/generator README 只留服务层 + 链 window/;script/README 家族表加 Window 行

## 结构规则沉淀

- **standard/ 目录规则**:函数无关代码骨架与文档模板并列存放;带 ⚠️ 标记的必须是合法标识符占位(`YourXxx`),⚠️ 不嵌入代码标识符(可编译承诺),注释标替换点
- **独特模板目录规则**:族目录只有 `README.md` 是 markdown,其余文件 = 代码+文本模板体;特征族文档需求并入族 README 段落
- 窗口壳不再是任何职责族的附庸:消费差异(bake/generate)由 window/README 差异表承载

## 未做(明确)

- standard/compute 代码模板(Pending 行保留)、shader/particle + render 占位不动、references/ 不改、Assets/** 不动
