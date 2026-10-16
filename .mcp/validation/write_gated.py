#!/usr/bin/env python3
"""写入门禁 CLI — 兼容入口（实现 = gate_cli.py 的 write 子命令）.

v2 三条操作里唯一没有 CLI 通道的就是写入 —— 于是 MCP 工具表一冻结，写入即断。
v3 补上，判定与记账同在 validation/judgement.py。

用法:
  python3 .mcp/validation/write_gated.py --path <项目相对路径> --from <本地文件> [--dry-run]
  python3 .mcp/validation/write_gated.py --path <p> -          # 内容读 stdin
退出码: 0 = 通过 ; 1 = 被拦或失败 ; 2 = 用法错误
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gate_cli import main  # noqa: E402

if __name__ == "__main__":
    sys.exit(main(["write", *sys.argv[1:]]))
