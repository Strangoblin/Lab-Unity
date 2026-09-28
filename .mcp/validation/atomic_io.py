"""原子写 —— write_gated 与 move_gated 共用。

同目录隐藏临时文件（`.` 前缀，Unity 忽略不导入）+ rename：Editor watcher 只看到
完整新内容，没有「截断 + 流式写入」的中间混合态（瞬时编译错误 / mtime 误报同属
这一竞争类）。

抽成独立模块而不是各写一遍：MCP 通道与 Bash 通道必须行为同源，
否则两条路会在「Editor 看到什么」这种细节上悄悄分叉。
"""

from __future__ import annotations

import os


def atomic_write(full_path: str, content: str) -> None:
    """原子地把 content 写到 full_path。失败时清理残留临时文件并抛出。"""
    tmp_path = os.path.join(os.path.dirname(full_path),
                            "." + os.path.basename(full_path) + ".uetmp")
    try:
        os.makedirs(os.path.dirname(full_path), exist_ok=True)
        with open(tmp_path, "w", encoding="utf-8") as f:
            f.write(content)
        os.replace(tmp_path, full_path)
    except Exception:
        try:   # 清理失败/中断残留的隐藏临时文件
            if os.path.isfile(tmp_path):
                os.remove(tmp_path)
        except Exception:
            pass
        raise
