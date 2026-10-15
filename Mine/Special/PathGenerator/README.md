# PathGenerator：独立路径生成与路径协议

路径生成负责把目标和环境转换成世界路径，不读取骨骼、不执行 Transform 位移，也不引用程序动作控制器。一个 `NavigationManager` 可以同时服务多个个体，路径以 `(ActorId, Epoch)` 隔离。

## 目录与接口

| 目录/类型 | 职责 |
| --- | --- |
| `Contracts` | 独立程序集 `CelToon.PathGenerator.Contracts`：请求、快照、路径点、状态、段类型、`IRouteProvider`、路径校验 |
| `Runtime/NavigationManager` | Unity 路径提供者，管理多个个体的路径、取消、释放及错误反馈 |
| `Runtime/GridPathGenerator` | 有界网格搜索；只依赖路径协议、Unity 数学类型和可替换环境接口 |
| `Runtime/INavigationWorld` | 线段与网格占用查询接口，可接入自己的地图或碰撞系统 |
| `Runtime/PhysicsNavigationWorld` | 默认 Unity Physics 查询适配器，障碍掩码默认第 10 层 |
| `Tests/EditMode` | 多个体/epoch 隔离、注入环境绕障及失败输入测试 |

`Runtime` 只引用 `Contracts`，不引用任何 `ProceduralController` 程序集。程序动作核心也只引用路径协议，不引用寻路实现。

## 使用

```csharp
provider.Submit(new RouteRequest {
    ActorId = actorId, Epoch = epoch, RequestId = requestId,
    StartWS = start, DestinationWS = destination, RequestedAt = now
});
if (provider.TryGetLatest(actorId, epoch, out var route)) {
    // 将 route 交给自己的移动、程序动作或其他消费者。
}
provider.Cancel(actorId, epoch);  // 不影响其他个体
provider.Release(actorId, epoch); // 个体销毁后释放缓存
```

`Epoch` 与 `RequestId` 必须非零，位置与时间必须有限。路径保持有效直到替换、取消或释放；相同个体的不同 epoch 可以并存，上层生命周期管理应及时 Release 旧 epoch。

不使用组件也可以直接调用网格生成器：

```csharp
var generator = new GridPathGenerator(myNavigationWorld, .5f, .3f);
bool found = generator.TryGenerate(start, destination, out var waypoints);
```

默认适配器先尝试直接线段，再搜索有界网格，禁止斜向穿过被占用的相邻格。保留原网格上限 96×96，超过范围或无法连通时返回失败，组件发布 `route.blocked`。这仍是测试用二维网格寻路，未增加 NavMesh、OffMesh、复杂地形或动态避障功能。替换寻路实现时实现 `IRouteProvider` 即可；替换环境查询时实现 `INavigationWorld` 并调用 `ConfigureWorld`。

## 移植

复制本目录及 `.meta` 即可使用路径生成；无需任何个体控制、IK、场景或项目 GraphicsSettings。可省略 `Tests`；核心只需 UnityEngine 与内置 Physics 模块。整个程序动作板块移植时，同时复制 `ProceduralController`。仅将协议作为外部路径接入时，只需本目录的 `Contracts`。

验证工具 `Docs/IKRebuild/Tools/verify_modules.py --build-projects --suffix <新名称>` 会生成只包含个体控制核心与路径协议的项目，以及只包含本模块的项目，供独立 Unity 测试使用。
