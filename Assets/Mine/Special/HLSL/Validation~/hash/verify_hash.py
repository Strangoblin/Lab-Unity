#!/usr/bin/env python3
"""Shared Hash verification helpers; standalone invocation runs the final contract.

Actual HLSL bodies are bridged for CPU comparisons; this is not Unity/Metal/GPU
validation. Immutable baselines live in fixtures; outputs stay in session tmp.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
FIXTURES = HERE / "fixtures"
HASH = "Assets/Mine/Special/HLSL/HashFunction.hlsl"
BLUR = "Assets/Mine/Special/HLSL/BlurFunction.hlsl"
WEATHER = "Assets/Mine/Shaders/PostProcess/Weather/WeatherCommon.hlsl"
SSO = "Assets/Mine/Shaders/PostProcess/SSO/SSO.shader"
FUR = "Assets/Mine/Shaders/Render/Fur/FinInstanced.shader"
PCSS = "Assets/Mine/Shaders/PostProcess/PCSS/PCSSFunction.hlsl"
INLINE = [f"Assets/Mine/Shaders/PostProcess/{x}/{x}.shader" for x in ("SSSM", "SSC", "SSL")]
ROOT = OUTPUT = GLSLANG = CLANG = None


def project_root(explicit=None):
    """Find the Unity repository, skipping partial fixture projects."""
    def valid(path):
        return (path / "AGENTS.md").is_file() and (path / HASH).is_file()
    configured = explicit or os.environ.get("HASH_PROJECT_ROOT")
    if configured:
        path = Path(configured).expanduser().resolve()
        if not valid(path):
            raise ValueError("Project root must contain AGENTS.md and Assets/Mine/Special/HLSL/HashFunction.hlsl")
        return path
    for path in (HERE, *HERE.parents):
        if valid(path):
            return path
    raise ValueError("Cannot find Unity project root; set HASH_PROJECT_ROOT or --project-root")


def executable(env_name, fallback):
    configured = os.environ.get(env_name)
    candidate = shutil.which(configured or fallback)
    if not candidate:
        raise OSError(f"Missing {fallback}; install it on PATH or set {env_name} to its executable")
    return Path(candidate).resolve()


def configure(argv=None, *, need_compilers=True, label="hash-validation"):
    global ROOT, OUTPUT, GLSLANG, CLANG
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", help="Unity repository root; alternatively HASH_PROJECT_ROOT")
    parser.add_argument("--output-dir", help="Generated files directory; default tmp/sessions/<run-id>/output")
    parser.add_argument("--run-id", help="Session folder name used by the default output directory")
    if need_compilers:
        parser.add_argument("--strict-migration-snapshot", action="store_true", help="Also reject later unrelated whole-file changes since the historical migration snapshot")
    args = parser.parse_args(argv)
    ROOT = project_root(args.project_root)
    run_id = args.run_id or datetime.now(timezone.utc).strftime("%Y-%m-%d-%H%M%S") + f"-{label}-{os.getpid()}"
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", run_id):
        raise ValueError("run-id must be a single safe directory name")
    OUTPUT = Path(args.output_dir).expanduser().resolve() if args.output_dir else ROOT / "tmp/sessions" / run_id / "output"
    if OUTPUT == HERE or HERE in OUTPUT.parents:
        raise ValueError("Output must not be inside the validation archive")
    sessions = (ROOT / "tmp/sessions").resolve()
    if OUTPUT == sessions or sessions not in OUTPUT.parents:
        raise ValueError("Output must be inside a task directory under project tmp/sessions")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    if need_compilers:
        GLSLANG = executable("GLSLANG_VALIDATOR", "glslangValidator")
        CLANG = executable("CLANG", "clang")
    return args


def cpp_compiler():
    # The original bridge requires a C++ standard library at link time.
    library = "-lc++" if platform.system() == "Darwin" else "-lstdc++"
    return [CLANG, "-x", "c++", "-std=c++17", "-O0", "-ffp-contract=off"], library


def run(args, expect_ok=True):
    p = subprocess.run([str(x) for x in args], cwd=ROOT, capture_output=True, text=True)
    if (p.returncode == 0) != expect_ok:
        raise AssertionError(f"Unexpected command result ({p.returncode}): {args}\n{p.stdout}{p.stderr}")
    return p

def no_comments(source):
    return re.sub(r"/\*.*?\*/|//[^\n]*", "", source, flags=re.S)

def function(source, name):
    source = no_comments(source)
    m = re.search(rf"\b(?:float[234]?|half[234]?|uint[234]?)\s+{re.escape(name)}\s*\([^)]*\)\s*(?::\s*\w+\s*)?\{{", source)
    if not m:
        raise AssertionError(f"Missing function: {name}")
    depth, end = 1, m.end()
    while depth:
        if end >= len(source):
            raise AssertionError(f"Unclosed function: {name}")
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[m.start():end]

def inline_jitter(source):
    m = re.search(r"\bfloat(?:3)?\s+jitter\s*=\s*([^;]+);", no_comments(source))
    if not m:
        raise AssertionError("Missing original/current inline jitter expression")
    return m.group(1)

def tokens(source):
    # Compare all code tokens, not titles/formatting/comments. New include is
    # audited separately for availability before use in actual Assets sources.
    source = no_comments(source)
    source = re.sub(r'#\s*include\s+"Assets/Mine/Special/HLSL/HashFunction\.hlsl"', "", source)
    return re.findall(r'"(?:\\.|[^"\\])*"|0[xX][0-9a-fA-F]+[uU]?|(?:\d+\.\d*|\.\d+|\d+)(?:[eE][+-]?\d+)?[fFuU]?|[A-Za-z_]\w*|>>|<<|\^=|\*=|\+=|-=|==|!=|<=|>=|&&|\|\||[^\s]', source)

def replace_tokens(source_tokens, old, new, expected_count=1):
    old, new = tokens(old), tokens(new)
    out, i, count = [], 0, 0
    while i < len(source_tokens):
        if source_tokens[i:i + len(old)] == old:
            out.extend(new)
            i += len(old)
            count += 1
        else:
            out.append(source_tokens[i])
            i += 1
    if count != expected_count:
        raise AssertionError(f"Expected {expected_count} replacement(s), found {count}: {' '.join(old)}")
    return out

BRIDGE = r'''
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
typedef float float2 __attribute__((ext_vector_type(2)));
typedef float float4 __attribute__((ext_vector_type(4)));
typedef uint32_t uint;
typedef uint32_t uint2 __attribute__((ext_vector_type(2)));
#define float2(...) ((float2){__VA_ARGS__})
#define float4(...) ((float4){__VA_ARGS__})
#define uint2(...) ((uint2){__VA_ARGS__})
static float frac(float x) { return x - floorf(x); }
static float2 frac(float2 x) { return float2(frac(x.x), frac(x.y)); }
static float4 frac(float4 x) { return float4(frac(x.x), frac(x.y), frac(x.z), frac(x.w)); }
static float4 sin(float4 x) { return float4(sinf(x.x), sinf(x.y), sinf(x.z), sinf(x.w)); }
static float dot(float2 a, float2 b) { return a.x * b.x + a.y * b.y; }
static int checks = 0, mismatches = 0;
static uint32_t bits(float x) { uint32_t b; std::memcpy(&b, &x, sizeof b); return b; }
static void equal(float a, float b, const char* label) {
    ++checks;
    if (bits(a) != bits(b)) {
        if (mismatches < 5) std::printf("MISMATCH %s %.9g %.9g [0x%08x 0x%08x]\n", label, a, b, bits(a), bits(b));
        ++mismatches;
    }
}
static void equal(float2 a, float2 b, const char* label) { equal(a.x,b.x,label); equal(a.y,b.y,label); }
static void equal(float4 a, float4 b, const char* label) { equal(a.x,b.x,label); equal(a.y,b.y,label); equal(a.z,b.z,label); equal(a.w,b.w,label); }
'''

def cpp(hlsl):
    text = no_comments(hlsl)
    # Match complete numeric tokens so hex integers, uint suffixes and vector
    # dimensions do not change. scalar sin becomes sinf, avoiding C++ doubles.
    numeric = r"(?<![\w.])((?:\d+\.\d*|\.\d+)(?:[eE][+-]?\d+)?|\d+[eE][+-]?\d+)(?![\w.])"
    text = re.sub(numeric, lambda m: m.group(1) + "f", text)
    text = re.sub(r"\bsin\(", "hlsl_sin(", text)
    return text

def renamed(hlsl, name, replacement):
    return re.sub(rf"\b{re.escape(name)}\b", replacement, hlsl)

def literal(x):
    s = repr(float(x))
    return s + "f"

if __name__ == "__main__":
    import verify_hash_final
    sys.exit(verify_hash_final.cli())
