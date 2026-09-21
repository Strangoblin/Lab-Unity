#!/usr/bin/env python3
"""模板 API 核验 CLI — 「模板里写下的 API 确实存在且没过时」的机械检查.

为什么是 API 核验而不是「必须能编译」:
  模板是给人**拷贝**的参考实现, 不要求自足编译(骨架本就有占位符, 且 .shader/.hlsl
  根本没法用编译器验)。但**任意一行错 API 会复制进每一份拷贝**, 这才是要挡的。
  本检查无「完整性」前提, 且 .cs / .shader / .hlsl / .compute 一视同仁。

检查项:
  1. 词法  — 标识符内出现非 ASCII 字符(注释与字符串之外)。零误报。
             (2026-09-20 实测: `class ⚠️YourEffectPass` 实编译报 CS1056)
  2. 过时  — 文件引用的名字若在 URP / Core RP 包源码里带 [Obsolete(..., true)],
             即为绑定错误(编译不通过)。以声明处为准, 不靠记忆。
             (2026-09-20 实测: [VolumeComponentMenuForRenderPipeline] 自 2023.1 起
              过时且 error: true, 而项目规范曾把它写成「Unity 6 的正确写法」)
  3. 编译  — --compile 追加, 仅适用于自足 .cs 骨架; 抓凭空 API 与其余一切。
             需本机有 dotnet; 引用路径见下方 resolve_refs()。

用法:
  python .mcp/validation/check_api_refs.py <path> [<path> ...]
  python .mcp/validation/check_api_refs.py --compile <path.cs>

退出码:
  0 = 通过(警告不阻断)
  1 = 有阻断项
  2 = 用法错误
"""

from __future__ import annotations

import os
import re
import sys

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

# 代码类文件(可做词法检查)——README/规范等 .md 不在此列
CODE_EXTS = (".cs", ".shader", ".hlsl", ".compute")

# 包源码: URP / Core RP 的 Runtime 源码, 过时检测的唯一权威(以声明处为准, 不靠记忆)
PKG_PREFIXES = (
    "com.unity.render-pipelines.core",
    "com.unity.render-pipelines.universal",
)

DECL_RE = re.compile(
    r"\b(?:class|struct|enum|interface|delegate)\s+([A-Za-z_]\w*)"
    r"|\b(?:public|internal|protected)\s+(?:static\s+|readonly\s+|sealed\s+|partial\s+|override\s+|virtual\s+|abstract\s+)*"
    r"[\w<>\[\],\.\s]+?\s+([A-Za-z_]\w*)\s*[\(;{=]"
)
OBSOLETE_RE = re.compile(r"\[Obsolete\s*\(")


def strip_comments_and_strings(text: str) -> str:
    """去掉 // 注释、/* */ 注释、字符串与字符字面量, 保留其余字符与换行结构.

    逐字符状态机而非正则——转义引号与跨行块注释用正则都容易出错。
    """
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == "/" and i + 1 < n:
            if text[i + 1] == "/":
                while i < n and text[i] != "\n":
                    i += 1
                continue
            if text[i + 1] == "*":
                i += 2
                while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                    out.append("\n" if text[i] == "\n" else " ")
                    i += 1
                i += 2
                continue
        if c == "@" and i + 1 < n and text[i + 1] == '"':      # 逐字字符串
            i += 2
            while i < n:
                if text[i] == '"':
                    if i + 1 < n and text[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            out.append('""')
            continue
        if c in ('"', "'"):
            quote = c
            i += 1
            while i < n:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == quote:
                    i += 1
                    break
                if text[i] == "\n":                             # 未闭合, 放弃
                    break
                i += 1
            out.append('""' if quote == '"' else "''")
            continue
        out.append(c)
        i += 1
    return "".join(out)


def build_obsolete_index() -> dict:
    """扫包源码, 返回 {名字: 是否 error 级过时}.

    声明形态(实测): [Obsolete(@"...", true)] 紧跟 public class Xxx。
    Obsolete 与声明之间可能夹 XML 注释/其它特性, 故向后看若干行。
    """
    index = {}
    cache = os.path.join(PROJECT_ROOT, "Library", "PackageCache")
    if not os.path.isdir(cache):
        return index
    for pkg in os.listdir(cache):
        if not pkg.startswith(PKG_PREFIXES):
            continue
        runtime = os.path.join(cache, pkg, "Runtime")
        if not os.path.isdir(runtime):
            continue
        for dirpath, _, files in os.walk(runtime):
            for fn in files:
                if not fn.endswith(".cs"):
                    continue
                try:
                    with open(os.path.join(dirpath, fn), encoding="utf-8", errors="replace") as f:
                        lines = f.read().splitlines()
                except OSError:
                    continue
                for ln, line in enumerate(lines):
                    if not OBSOLETE_RE.search(line):
                        continue
                    if not re.search(r",\s*true\s*\)", line):   # 只看 error 级
                        continue
                    # Obsolete 与声明之间可能夹 XML 注释/其它特性, 故向后看若干行
                    for probe in lines[ln : ln + 8]:
                        m = DECL_RE.search(strip_comments_and_strings(probe))
                        if m:
                            name = m.group(1) or m.group(2)
                            if name:
                                index.setdefault(name, True)
                            break
    return index


def check_lexical(path: str, raw: str) -> list:
    """标识符内非 ASCII — 注释与字符串之外。"""
    problems = []
    stripped = strip_comments_and_strings(raw)
    for ln, line in enumerate(stripped.splitlines(), 1):
        bad = [ch for ch in line if ord(ch) > 127]
        if bad:
            problems.append({
                "check": "词法",
                "detail": "标识符内出现非 ASCII 字符 %s —— 编译器会报非法字符"
                          % " ".join(repr(c) for c in sorted(set(bad))),
                "line": ln,
            })
    return problems


def check_obsolete(path: str, raw: str, index: dict) -> list:
    if not index:
        return []
    problems = []
    stripped = strip_comments_and_strings(raw)
    seen = set()
    for ln, line in enumerate(stripped.splitlines(), 1):
        for name in re.findall(r"\b[A-Z][A-Za-z0-9_]*\b", line):
            if name in index and name not in seen:
                seen.add(name)
                problems.append({
                    "check": "过时",
                    "detail": "`%s` 在包源码中标记为 [Obsolete(..., true)] —— 编译不通过, "
                              "需改用其推荐的替代 API" % name,
                    "line": ln,
                })
    return problems


def resolve_refs() -> tuple:
    """返回 (UnityEngine 模块目录, 项目 ScriptAssemblies 目录)。

    URP 程序集取项目 Library/ScriptAssemblies(最可靠, 勿猜 Unity 安装路径——
    按旧印象写 Contents/Managed/ 会得到 30 个 CS0246, 看着像模板坏了其实是路径错)。
    """
    unity = None
    for cand in ("/Applications/Unity",):
        if not os.path.isdir(cand):
            continue
        for ver in sorted(os.listdir(cand), reverse=True):
            p = os.path.join(cand, ver, "Unity.app/Contents/Resources/Scripting/Managed/UnityEngine")
            if os.path.isdir(p):
                unity = p
                break
        if unity:
            break
    return unity, os.path.join(PROJECT_ROOT, "Library", "ScriptAssemblies")


def check_compile(path: str) -> list:
    """自足 .cs 骨架的真编译(一次性 csproj, 不改仓库、不触发域重载)。"""
    import subprocess
    import tempfile

    unity, scripts = resolve_refs()
    if not unity or not os.path.isdir(scripts):
        return [{"check": "编译", "detail": "找不到 Unity/URP 程序集, 跳过编译检查", "line": 0}]

    refs = [
        ("UnityEngine.CoreModule", os.path.join(unity, "UnityEngine.CoreModule.dll")),
        ("Unity.RenderPipelines.Core.Runtime", os.path.join(scripts, "Unity.RenderPipelines.Core.Runtime.dll")),
        ("Unity.RenderPipelines.Universal.Runtime", os.path.join(scripts, "Unity.RenderPipelines.Universal.Runtime.dll")),
    ]
    ref_xml = "".join(
        '<Reference Include="%s"><HintPath>%s</HintPath></Reference>' % (n, p)
        for n, p in refs if os.path.isfile(p)
    )
    proj = (
        '<Project Sdk="Microsoft.NET.Sdk">'
        "<PropertyGroup><TargetFramework>netstandard2.1</TargetFramework>"
        "<EnableDefaultCompileItems>false</EnableDefaultCompileItems>"
        "<NoWarn>CS0169;CS0649;CS0414;CS0108</NoWarn></PropertyGroup>"
        '<ItemGroup><Compile Include="%s" />%s</ItemGroup></Project>' % (os.path.abspath(path), ref_xml)
    )
    with tempfile.TemporaryDirectory() as tmp:
        with open(os.path.join(tmp, "check.csproj"), "w", encoding="utf-8") as f:
            f.write(proj)
        try:
            out = subprocess.run(["dotnet", "build", "-v", "q", "--nologo"],
                                 cwd=tmp, capture_output=True, text=True, timeout=300).stdout
        except (OSError, subprocess.SubprocessError) as e:
            return [{"check": "编译", "detail": "dotnet 不可用: %s" % e, "line": 0}]
    problems, seen = [], set()
    for line in out.splitlines():
        if "error CS" not in line:
            continue
        msg = line.strip().split(" [")[0].strip()          # 去掉临时 csproj 路径尾巴
        if msg not in seen:                                # dotnet 会重复报同一条
            seen.add(msg)
            problems.append({"check": "编译", "detail": msg, "line": 0})
    return problems


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    do_compile = "--compile" in sys.argv

    if not args:
        print(__doc__)
        return 2

    files = []
    for a in args:
        if os.path.isdir(a):
            for dirpath, _, names in os.walk(a):
                files += [os.path.join(dirpath, n) for n in names if n.endswith(CODE_EXTS)]
        elif os.path.isfile(a):
            files.append(a)
        else:
            print("ERROR: 路径不存在: %s" % a)
            return 2

    index = build_obsolete_index()
    if not index:
        # 静默空转是这层最危险的失效模式(检查 2 会假装通过)。宁可吵。
        print("⚠ 过时 API 索引为空 —— 检查 2 未生效。确认 Library/PackageCache/ 下存在 "
              "%s* 包且含 Runtime/ 源码。" % " / ".join(PKG_PREFIXES))
    total = 0
    for path in sorted(files):
        with open(path, encoding="utf-8") as f:
            raw = f.read()
        problems = check_lexical(path, raw) + check_obsolete(path, raw, index)
        if do_compile and path.endswith(".cs"):
            problems += check_compile(path)
        # 控制组常喂项目外副本(/tmp), 此时 relpath 会得到一串 ../../, 直接显示绝对路径更可读
        ap = os.path.abspath(path)
        rel = os.path.relpath(ap, PROJECT_ROOT)
        if rel.startswith(".."):
            rel = ap
        if problems:
            total += len(problems)
            print("✗ %s — %d 项" % (rel, len(problems)))
            for p in problems:
                loc = " L%d" % p["line"] if p["line"] else ""
                print("    [%s]%s %s" % (p["check"], loc, p["detail"]))
        else:
            print("✓ %s" % rel)

    if total:
        print("\n%d 项阻断 (exit 1)" % total)
        return 1
    print("\n全部通过 (exit 0)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
