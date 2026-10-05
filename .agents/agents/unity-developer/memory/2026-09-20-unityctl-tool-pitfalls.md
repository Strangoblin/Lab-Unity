---
name: unityctl-tool-pitfalls
description: unityctl 的四个机制坑：screenshot capture 坏、editor run/stop 非 Play Mode、script eval 编译错误静默、editor run 在本机找不到 Unity（非 Hub 布局）。
date: 2026-09-20
---

# 2026-09-20 — unityctl 工具的坑（取像 / 生命周期 / 求值 / 启动路径）

> 本文件是 **unityctl 使用层踩坑的唯一权威位置**。
> 画面与 RT 的**判读方法论**在 [2026-09-20-rt-readback-pitfalls.md](2026-09-20-rt-readback-pitfalls.md)。

## 坑 1 — `screenshot capture` 不可靠，取像一律走 `screenshot window GameView`

**2026-09-20 实机复现**（Editor 已连接、Play Mode stopped，共 5 次尝试）：

| 命令 | 报出的 Resolution | 落盘 | 产物 |
|---|---|---|---|
| `screenshot capture [path]` | `2798x72` / `1288x1138` | 5 次中 **1 次** | 落盘那次 642×522 **有效 PNG** |
| `screenshot window GameView <path>` | `642x543` | 每次都落 | ✅ 有效，路径正确 |

四个可复现特征：

1. **报出的 Resolution 是垃圾值，且与产物对不上**。它先后报 `2798x72`、`1288x1138`，
   而真正（偶尔）写出的文件是 **642×522** —— 报的尺寸和产物不是同一张图。
   **这个数字不可信，不能拿它做任何判断。**
2. **落盘不可靠**。5 次只成功 1 次；成功那次也是在 CLI 打完
   `Warning: screenshot file not written after 5s` **之后**才出现 —— 它的 5s 等待不够。
3. **传入的目录被丢弃**。传 `/tmp/x.png`，Warning 暴露的真实落点是
   `<project>/Screenshots/x.png`，只保留 basename。而 stdout 的
   `Screenshot captured: /tmp/x.png` **回显的是你的输入、不是真实落点** ——
   照着它去 `/tmp` 找必然扑空，且极易误读成「写成功、写在 /tmp」。
4. `window GameView` 侧无以上任一问题：取的是 **Editor 窗口**，
   路径与尺寸都对，**Edit Mode 下同样可用**，不必进 Play Mode。

**结论**：需要画面时用 `unityctl screenshot window GameView <绝对路径>`。
`capture` 不值得抢救 —— 一个连自己产物尺寸都报错的命令，其成功与否不可预测。

> ⚠️ **本条的初版写错了，作为反面教材保留**：我由一次「文件没出现」直接断言
> 「**文件从不落盘**」。后来复查 `Screenshots/` 才发现有一次其实写成功了。
> **单次否定观察不足以下「从不/恒为」这种全称断言** —— 这正是
> [2026-09-20-rt-readback-pitfalls.md](2026-09-20-rt-readback-pitfalls.md) 陷阱 3 的同一形态，
> 在**同一天内第二次**复发，只是对象从 RT 换成了工具输出。

> 项目约定仍是视觉结果由人工在 Game View 观察；此条只解决「确实需要留一张图」的场景。
> 另外 `Screenshots/` 已在 `.gitignore`，落盘失败也不会污染仓库。

## 坑 2 — `editor run/stop` 是进程生命周期，不是 Play Mode

`play` 与 `editor` 两组命令名字太像，但用错的代价极不对称：

| 想做的事 | 正确命令 | 备注 |
|---|---|---|
| 进入播放 | `unityctl play enter`（别名 `start`） | — |
| 退出播放 | `unityctl play exit`（别名 `stop`） | — |
| 启动 Editor 进程 | `unityctl editor run`（别名 `launch/open/start`） | 不是「进播放模式」 |
| **终止 Editor 进程** | `unityctl editor stop`（别名 `close/kill`） | **真的会关掉 Editor** |

`editor stop` 终止的是 **Editor 进程本身**。有未保存内容时这一步不可逆。
「想退出播放模式，手误打成 `editor stop`」是这组命令里最贵的一次误打。

## 坑 3 — `script eval` 的编译错误不回显真错

编译失败时只回显「wrapper + 泛泛的 hint（Expressions are wrapped in return...）」，
**不显示真正的编译错误**，极易被误读成语法问题。

- `-u/--using` 加的是 **using 指令，不是程序集引用**。类型找不到时改用**完全限定名**
  （如 `UnityEngine.Rendering.Universal.ScriptableRendererData`），比 `-u` 可靠。
- 别用非 ASCII 变量名 —— 中文标识符会显著加剧诊断难度。
- 多语句写 `var x = ...; return ...;`；单表达式**不要**再写 `return`，会与自动包裹的 `return` 冲突。

## 坑 4 — `editor run` 在本机找不到 Unity（非 Hub 布局，必须带 `--unity-path`）

本机 Unity 装在 `/Applications/Unity/Unity-6000.3.14f1/Unity.app`，
**不是** Hub 的 `/Applications/Unity/Hub/Editor/<version>/`；`unityctl editor run` 只按 Hub 默认路径查找，
因此未带路径时必然报：

```text
Error: Unity 6000.3.14f1 not found in Unity Hub.
  Expected locations:
    /Applications/Unity/Hub/Editor/6000.3.14f1
  Use --unity-path to specify the Unity executable manually.
```

`/Applications/Unity/Hub/Editor/` 在该机器上**根本不存在**，
`~/Library/Application Support/UnityHub/secondaryInstallPath.json` 为空字符串。

**结论**：本机启动 Editor 一律带路径；找不到路径时用
`mdfind "kMDItemCFBundleIdentifier == 'com.unity3d.UnityEditor5.x'"` 定位。

```bash
unityctl editor run --unity-path /Applications/Unity/Unity-6000.3.14f1/Unity.app
```

> **未定因，不要据此断言 unityctl 或 Unity 损坏**：2026-09-21 用 `--unity-path` 启动后，
> CLI 报出 `Unity started (PID: …)`，Unity 也完成了 licensing（Unity Personal、Expiration Unlimited），
> 随后进程即退出。`~/Library/Logs/Unity/Editor.log` 里 `COMMAND LINE ARGUMENTS` 段
> **只有二进制路径、没有 `-projectPath`**，项目加载与资源导入段完全没发生；
> 预期落点 `.unityctl/editor.log` 从未被创建（`.unityctl/` 下仍只有 8 月的 `editor-prev.log`），
> 无 `Temp/UnityLockfile`。同一次尝试里 bridge（真实宿主，已运行 4h+）始终报 `Unity not connected`，
> 与「沙盒 bridge 与真实宿主 Editor 不互通」的既有边界一致，但**该次启动失败的根因未确认**。
