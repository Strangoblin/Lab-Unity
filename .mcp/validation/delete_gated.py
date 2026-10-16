#!/usr/bin/env python3
"""删除门禁 CLI — 兼容入口（实现 = gate_cli.py 的 delete 子命令）.

判定同源: validation/judgement.py。独立实现已删除，两个通道不再各判一套。

用法:
  python3 .mcp/validation/delete_gated.py --reason "<原因>" [--recursive] [--dry-run] <path>...
空目录默认可删；非空目录须 --recursive，子项仍逐一预检。
退出码: 0 = 成功/可删 ; 1 = 被拦 ; 2 = 用法错误
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gate_cli import main  # noqa: E402

if __name__ == "__main__":
    sys.exit(main(["delete", *sys.argv[1:]]))
