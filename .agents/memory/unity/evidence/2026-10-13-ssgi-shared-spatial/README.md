# 共享空间重建证据

所有者：unity-developer的2026-10-13-ssgi-shared-spatial-reconstruction.md。

- final-results.json：49组/1,152,535断言，GPU/CPU空间参考、raw RAY及冻结Specular回归、入口/历史。
- distance-control.json / history-control.json / spatial-control.json：三个已知错误确实被识别。
- noise-radius2.json：初版固定条件统计，非最终版本；noise-final.json：最终步幅1.5但用户已改变镜头/时间，不能直接A/B旧数字。
- diffuse-before.png / diffuse-radius2.png / diffuse-final.png：640x360隔离空间显示，已打开；不能证明真实动态画质。radius2图为说明已拒绝的奇偶子网格候选，不保留并行生产源码。
- live-final.json / protected-before.sha1：当前原配置与保护文件。CLI内tmp路径仅作当时来源记录。
- gate-contract-report.json：隔离E5通过、真实state/audit保持。

依赖当日Unity6000.3.14f1/URP17.3/M2 Metal、测试预览几何和报告源设置。复跑从功能Validation~/run.sh开始，新结果写tmp/sessions，不覆盖本目录；这些是该版本证据，不是新版本验收保证。未来无复现/历史基准价值时按精确清单人工确认清理。
