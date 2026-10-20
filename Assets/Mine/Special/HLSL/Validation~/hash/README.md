# Hash 复用验证

正式入口：`Assets/Mine/Special/HLSL/Validation~/hash/`（Lab 项目专属适配器；资源绑定 `validation.hash.guide` / `validation.hash.adapter`）。
精选报告位于 `.agents/memory/unity/evidence/2026-10-10-hash-function/`。
2026-10-10 已按批准清单安装、从正式路径复跑并移除对应旧临时文件。

## 文件与依赖

- `verify_hash.py`：公共解析/CPU bridge helper；直接执行时进入当前最终验证。
- `verify_hash_final.py`：当前 Hash 算法、调用合同、HLSL 编译和故障对照。
- `analyze_variants.py`：原式 float32 转写的代表输入域统计。
- `fixtures/before.json`、`fixtures/final-stage-before.json`：不可变原始基线，必须随脚本归档。
- `source-map.json`：精确来源、目标、SHA-256 与改编说明。
- 依赖 Python 3、Clang（向量扩展/C++17）、glslangValidator；统计还需 NumPy。
- 编译器从 PATH 查找，也可设置 `CLANG`、`GLSLANG_VALIDATOR`。不写死安装路径。
- 项目根沿脚本父级查找，也可设 `HASH_PROJECT_ROOT` 或传 `--project-root`。

## 重跑

从项目根执行：

```sh
python3 Assets/Mine/Special/HLSL/Validation~/hash/verify_hash.py \
  --run-id 2026-10-10-hash-validation-rerun
python3 Assets/Mine/Special/HLSL/Validation~/hash/analyze_variants.py \
  --run-id 2026-10-10-hash-statistics-rerun
```

默认输出在 `tmp/sessions/<run-id>/output/`；同一任务可传
`--output-dir tmp/sessions/<existing-task>/output/<run-name>`。
输出必须在项目 `tmp/sessions/` 的任务目录中，不能写入归档、Assets 或配置目录。

## 当前验证与历史证据的区别

2026-10-10 重跑通过：63,212 次 CPU 比较，0 不匹配，54,161 次范围检查；
13 个实际 Shader/Compute 入口 include 顺序通过；float target 宏 20/35/50、
uint 35/50 编译/SPIR-V 校验通过，uint 20 按预期拒绝。
Sine21、FracGrid22 故障副本分别检出 14,735、22,492 次不匹配。

历史整文件迁移快照有 15 个文件仍保持；SSL、SSO、Kuwahara、DDOF 后续修改
已在新报告列为历史漂移，不声称这些文件整文件未变。
当前检查保留完整标准 Hash 库、精确 wrapper/Hash 调用语句，以及 SSL/SSO
完整有效 Hash 消费函数。SSL 允许已核对的 wrapper 删除/direct call；SSO
允许已证明未使用的局部 jitter 声明删除。新漂移路径仍会失败。
`--strict-migration-snapshot` 保留严格历史检查；当前树按预期拒绝上述漂移。

历史最终验证报告与当前合同报告分别保存；原始基线不修改。
统计报告只读取原式基线，报告的零差异不是独立的 HLSL 执行等价证明。
本次额外 10 个检查证实时间键、列键、下游幅度/寿命修改和保护路径会被拒绝。

## 证据边界

没有 Unity Editor/Metal 编译、GPU 执行或画面对照。CPU/SPIR-V 检查不能证明
跨平台浮点一致、随机独立性或视觉等价。RainDrop、SSL 径向、VanGogh 2D
具体序列有意改变；少量列速度与格子布局可能改变。

证据目录里的 `VARIANT_REVIEW.md` 记录输入域、随机最终作用、合并与保留理由。

## 归属与适用范围

本验证器是 Lab Hash 迁移合同的项目专属适配器，原式路径是不可变基线键，不作为通用技能的默认资产布局。整体与 fixtures 保持配对；source-map.json 是 2026-10-10 的来源快照，本次技能归属变更另记 meta memory，不改写历史 SHA。其他项目不得直接套用这些消费函数合同。
