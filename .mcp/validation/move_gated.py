#!/usr/bin/env python3
"""移动门禁 CLI — 兼容入口（实现 = gate_cli.py 的 move 子命令）.

判定同源: validation/judgement.py（moving.plan + moving.execute）。独立实现已删除。

用法:
  python3 .mcp/validation/move_gated.py --reason "<原因>" [--dry-run] <from> <to> ...
退出码: 0 = 成功/可移动 ; 1 = 被拦 ; 2 = 用法错误
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gate_cli import main  # noqa: E402

if __name__ == "__main__":
    sys.exit(main(["move", *sys.argv[1:]]))
