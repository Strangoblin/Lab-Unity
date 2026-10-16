---
name: render-hlsl-template-families
description: 2026-09-03 模板体系第四轮:shader/render 占位落地(直写单 Pass + 复杂材质对) + shader/hlsl 新家族(对应 Special/HLSL 共享库,三档依赖决策表)。
metadata:
  type: architecture
---

# Render 家族落地 + HLSL 新家族 — 2026-09-03

## Context

模板重分类(见 [[template-reference-taxonomy]])后 shader/ 只有 postprocess 落地,render/ 是三行占位。用户指令:填 render(素材 `Assets/Mine/Shaders/Render/` 24 shader + 5 本地库)+ 新开 hlsl 家族(素材 `Assets/Mine/Special/HLSL/` 18 共享库)。三 Explore agent 全量盘点后用户裁定三项(全取推荐)。

## 裁定与落地

1. **render 普通形态 = 直写效果材质·单 Pass**(`direct-effect.shader`):与 standard-shader.shader(生产三 Pass 材质骨架)互补 —— 无阴影/深度 Pass 义务、状态自由(Cull/Blend/ZWrite/ZTest ⚠️ 变体注释)、通用 Attributes/Varyings;普通存量多为旧写法(无横幅/lowercase)→ 按现行规范重写不作逐字源
2. **render 复杂形态 = 材质光照型对**(`effect-shader.shader` + `effect-function.hlsl`):InteriorMapping(09-02)主干 + TheStarryNightSDF guard/「依赖调用方」合同注释;**不做全屏面片型**(Cull Off 直绘 = 旧做法,新全屏走 postprocess 族,README 说明)。模板带「拷贝两步走」:双文件同拷 + include 路径同步(激活行注释态,全路径写法 + RainDrop 裸相对名实坑注)
3. **hlsl 新家族 = 1 通用模板 + README 决策表**(`function-lib.hlsl` 纯函数零依赖基线):对应 Special/HLSL 18 共享库;核心 = **三档依赖决策表**(A 纯函数 7 实源拷贝即用 / B 依赖调用方上下文不自行 include + 合同注释 6 / C 自带 include + 全局命名契约 5);SG Custom Function 形态(`_float/_half`)不落模板照实源改

## 结构规则沉淀

- **shader 族分工定稿**:standard = 函数无关骨架 / postprocess = 全屏 Blit 管线 / render = 物体上渲染的材质效果(边界句入族 README)/ hlsl = 跨效果共享库 / particle 保留
- **拆库裁决**:单效果私有数学/专属 SDF → 同目录私有库(自持 CBUFFER/static const 合法,InteriorMappingFunction 先例);跨效果横切 → Special/HLSL(**零 CBUFFER、零兄弟 include** 铁律)—— 私有/共享的边界 = 自持状态权利
- **hlsl 家族标准**:guard 统一 15/18 主风格(文件名去连字符全大写 + `_HLSL_INCLUDED`,与文件名绑定改名必同步);文件头 ═ 横幅必须(实源 13/18 缺 = 05-29 外部导入旧账);函数前缀语义模块化(避无前缀通用名)
- **警示入 README**:ShadowFunction 与 AdditionalLightsFunction 的 `MainLight` 函数体全同 —— 同 shader 双 include 必重复定义(潜伏地雷,现状无 shader 同时引入)
- 模板可编译承诺扩展:复杂对「双文件同拷 + include 同步」两步;⚠️ 只出现于注释与字符串,激活代码零内联 ⚠️

## 未做(明确)

references/ 不动(其 Render/particle "reserved" 行指 references 层,仍准确)、particle 占位不动、Assets/** 零写入(存量库/旧 shader 一律不动,只读实源)、Amplify 生成物(Error/PCGUI)不入模板源、细分曲面/实例化形态不入模板(README 实源指引)
