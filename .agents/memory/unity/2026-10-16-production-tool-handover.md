# 统一生产工具框架接手与回归

## 接手点

- 接续 2026-10-15 Generator / Baker 统一框架，以及 2026-10-16 模板入库、memory 写入规则任务。原始目标与实现见 [历史记录](2026-10-15-production-tool-framework.md)，体系部分见 [meta 记录](../meta/2026-10-16-write-channel-rules.md)。
- 历史最后中断于重复记录与暂存草稿清理后的收尾；本次确认正式记录、索引、production-tool 模板和资源绑定均已存在，不重做迁移。
- 沿用 `Assets/Mine/Scripts` 任务范围，经 Production 配方与工作区门禁写入。HEAD 仍为 `f9b3ad8`；大量既有脏改动未归并、未提交或推送。

## 实际变更

- `NoiseGeneratorWindow` 的 2D 打包预览原先会在尺寸/hash 不符时替换当前资产，导致 Load 的结果被当前参数覆盖。现在持久化的 Load / Save 结果保持原样，显式 Generate 才替换；未落盘 2D 预览仍按原行为随参数更新。
- 3D 切片原先只调用当前设置的程序化采样。现在有当前 Texture3D 时读取其真实像素，按三轴实际宽/高/深构建切片；换源释放切片缓存。尚无资产时保留参数预览；不可读资产显示提示，不伪造载入内容。
- 五份同目录工具文档本就存在，旧记录的“待补文档”不再作为当前待办。已同步 Curve / Noise / SSS 默认路径、FGD / InteriorMap 单一 Asset Path，以及 Noise 单一当前资产的准确换源语义。
- 新增功能专属 [验证入口](../../../Assets/Mine/Scripts/ProductionTools/Validation~/README.md)，通过反射执行按钮实际调用的方法，复用现有生产框架，不引入第二套 IO 实现。

## 已验证

- `unityctl -t 180 asset refresh`：compilation succeeded。
- Editor 内 `ProductionToolChecks.cs`：**PASS 37 checks**。覆盖五窗口 Load / self-Save、self-Save GUID/对象身份、瞬态资产保存克隆与释放、关窗不销毁持久化资产、Noise 显式 Generate、2D 载入/保存后预览稳定、三轴像素与非立方尺寸、同尺寸换源缓存失效、不可读 3D 输入、路径校验。
- 复现探针在旧实现中观察到 3D 像素/尺寸不符；修复后载入 2D 原生对象保持，3D 像素与尺寸正确。探针最初误用反射返回 object 的引用比较，已改为 UnityEngine.Object 比较；正式回归采用后者。
- `check_norm.py` 对本次源码、验证入口与文档逐文件通过；精确范围 `git diff --check` 通过。
- 收尾架构 `verify.py --strict` 与四套隔离 MCP 契约测试通过；隔离 runner 确认真实 `.mcp` state/audit 指纹未变。
- 并行 explorer 只读复核 Noise 修复；实际调用 OpenCode `opencode-go/deepseek-v4.1-flash` 一次，核对模板与基类的 8 项成员契约全部一致，worker 未改文件。
- 测试仅创建唯一 `__Validation_<guid>` 资产目录和临时窗口，finally 清理；收尾剩余 fixture 为 0。场景 dirty=false、未进入 Play。
- `SampleScene.unity` 与 `PC_Renderer.asset` 前后 SHA-256 一致，未调用全局 SaveAssets。

## 验证边界与后续

- 37 项检查调用按钮回调，不代表物理点击全路径验收；不同资产覆盖的确认/取消交互未代跑，GUID 实测口径是 self-Save。
- 未执行场景依赖的 InteriorMap Bake，未重验 FGD / SSS GPU 输出画质；生成服务算法未改。
- 保留 Noise 单一当前资产决策；若要改为两模式各持一份资产，仍属后续功能决定。
- Load 当前仍不按消费者校验资产类型，错误类型输入的提示与拒绝可另行收敛；本次未扩展公共抽象契约。
- 本次新增验证源码保留在功能 `Validation~/`；临时探针只作复现，正式入口归位后清理。旧 dated memory 保持原文，当前结论通过领域索引指向本记录。
