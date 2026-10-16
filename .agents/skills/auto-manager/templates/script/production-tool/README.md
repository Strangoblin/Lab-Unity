# Production Tool Templates

**定位**:Generator / Baker 一类资产产出窗口的完整标准模板。
[production-tool-window.cs](production-tool-window.cs) 直接继承 `EditorWindow`，包含窗口自己的路径、行动行、资产 IO 与所有权管理；
复制并替换业务占位符即可形成独立工具，项目无需安装共享基类、资产 IO 类或路径注册表。

agent 仅保留这份完整标准模板。项目五个生产工具窗口各自独立继承 `EditorWindow`，具体业务代码保留在工具本地；
新工具的生成算法沿用 [baker/](../baker/README.md) 或 [generator/](../generator/README.md) 服务层，设置与预览由具体窗口提供。
运行时场景编排组件用 [manager/](../manager/README.md)；纯 GUI 窗口见 [window/](../window/README.md)。

## 模板与工具的分工

| 内容 | 归属 |
|---|---|
| 标准路径行、行动行、资产 IO 与所有权管理 | agent 内的完整窗口模板，新窗口按需复制并调整 |
| Unity 实际编译的窗口 | 具体工具的 `sealed class XxxWindow : EditorWindow`，所需流程与 IO 为类内私有成员 |
| Save 与 Load 的路径 | 具体窗口自己的 `[SerializeField]` 字段，二者共用同一来源 |
| 生成算法 | 工具服务层静态类，窗口通过 `BakeCore()` 调用 |
| 设置与预览 | 具体窗口本地实现，不依赖其他工具窗口 |

通用契约在标准模板维护；已有工具的修改按任务范围审查和验证，不依赖项目级共享框架。
迁移前的原始框架与路径表只作为不可变历史证据保留，不是活动模板或新工具的路径来源。

## 需要替换与调整的成员

| 成员 | 要求 | 说明 |
|---|---|---|
| 类名、菜单与标题 | 必须 | 将 `YourToolWindow` 和 UI 占位文案替换为具体工具 |
| `_assetPath` | 必须 | 将 `"YOUR_AUTHORIZED_ASSET_PATH"` 替换为当前任务已授权的 `Assets/.../*.asset` 路径 |
| `BakeLabel` | 必须 | Baker 型 `"Bake"`，Generator 型 `"Generate"` |
| `BakeCore()` | 必须 | 调服务层并返回待保存资产，失败返回 `null` |
| `CanBake` | 按需 | 调整类内私有条件；场景输入未就绪时禁用 Bake |
| `ShowBakeProgress` | 按需 | Baker 型 true，Generator 型 false |
| `HasSaveableAsset` | 按需 | 多输出模式按当前模式判断可保存性 |
| `OnAssetChanged()` | 按需 | 生成 / 载入后刷新预览与状态 |

这些成员是完整窗口内部的私有实现，不是供项目继承的接口。根据业务调整设置、预览及上述条件时，保留保存与所有权契约。

**所有权**:`BakeCore()` 产物交给窗口，换源或关闭时释放；经 `AssetDatabase` 载入或落盘的资产归资产系统，窗口不销毁。
显示预览的缓存不能替换当前待保存或已载入的资产。自定义生命周期清理时须保留 `OnDisable()` 中的资产释放流程。

**保存**:已有同类型资产通过 `CopySerialized` 原位更新，保留 GUID 与外部引用；不以 Delete + Create 覆盖。
覆盖前确认，类型不符拒绝写入；只对目标资产调用 `SaveAssetIfDirty`，不保存会话中其他资产。

## 采用步骤

1. 将完整模板复制到 `tmp/sessions/<task>/staging/`，替换类名、菜单、标题与资产路径占位符。
2. 实现工具的 baker / generator 服务层与 `BakeCore()`，调整窗口设置、预览和私有条件。
3. 确认 Save 与 Load 共用窗口路径字段；路径由当前任务和资源接口确定，不能照抄历史 Lab 路径。
   Browse 只用于改绑同一字段，未替换的路径占位符不能通过资产路径校验。
4. 审核完整窗口及上述契约后，按现有任务授权与门禁合入 `<category.Script>/<Tool>/Editor/`，再做编译与功能验证。

GUI 脊柱统一为标题 → 设置 → 路径行 → 行动行 → 预览。模板里的示例服务与参数须替换后再进入项目。

## 与其他族的边界

| 族 | 关系 |
|---|---|
| [window/](../window/README.md) | 路由纯 GUI 与资产产出窗口；后者使用本族完整标准模板 |
| [baker/](../baker/README.md) | 一次性重烘焙服务层，窗口采用本族 Bake 形态 |
| [generator/](../generator/README.md) | 可再生资产服务层，窗口采用本族 Generate 形态 |
| [manager/](../manager/README.md) | 运行时场景编排，不承担资产产出窗口 |
