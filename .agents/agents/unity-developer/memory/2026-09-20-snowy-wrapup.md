---
name: Snowy 原型重构收尾
description: 统一噪声纹理、屏幕雪斑与气氛双 Pass 的最终文件与维护状态
date: 2026-09-20
---

# 当前产物

- Assets/Mine/Shaders/PostProcess/Snowy/Snowy.shader：PostProcess/Snowy，Pass 0 雪斑、Pass 1 气氛，共享 SnowTex.r。
- Mine_PostProcess_Snowy.mat：绑定 Assets/Textures/Noise2DRG.asset；清理已失效的旧参数，仅保留当前 Shader 属性。
- README.md：按用户最终实现重写，修正旧六层/十层独立粒子与生长周期描述。
- TRS.hlsl 继续作为共享变换库；效果逻辑不再拆私有 HLSL。

# 最终实现与边界

当前粒子为单一周期随机落点的噪声扰动 SDF，十次相同调用叠加覆盖；不是十个独立粒子。收尾只同步注释、材质和文档，不改用户视觉公式。气氛保留霜边、深度纹理雾、冷调色，float4 混合也影响 Alpha。

DebugOutputFeature 测试入口：Additional Pass Index=1，Require Depth=true，配置必须保存；先前仅内存设置曾导致重新加载后第二 Pass 不执行。

参数端点统一入口约束沿用 2026-09-18-parameter-range-policy.md，不追加逐点保护。

# 整理状态

旧 SnowyParticle.png 及其 .meta 的 GUID 搜索未发现其他 Assets 引用，已于用户明确确认后删除，README 已同步。未提交 Git。
