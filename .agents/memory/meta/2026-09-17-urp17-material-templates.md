---
name: Unity 6 URP 17 网格材质模板升级
description: 标准四 Pass 与轻量单 Pass 模板补齐实例化和管线契约
date: 2026-09-17
---

# 网格材质模板升级

- 在现有 standard/shader/README.md 定义能力矩阵，Render README 与结构 reference 仅引用；不新建平行模板家族。
- 标准模板四 Pass 共用位置、法线和 AlphaClip；支持经典实例化、Stereo 基础接口、主光阴影、SH、雾与 URP 法线编码。
- 两个 Render 单 Pass 模板补齐实例化/Stereo 和 ForwardOnly，保持单 Pass 的职责边界。
- 修正硬规则中“Metal 必须 target 2.0”的不准确限制；新网格模板明确 target 3.0。
- 本地 Metal GPU 对照：三个模板独立绘制与双实例输出差为 0；标准四 Pass 的 AlphaClip 轮廓及实例化深度一致。
- 显式绘制测试必须提供需要的 SH/探针数据；初次测试缺少数据造成明暗差，补齐后通过。
- P1-P3：复用现有代码模板和 README，能力事实单源，测试脚本只留 tmp；平台薄适配共用规范。
