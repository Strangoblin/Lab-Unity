"""项目路径规则 — 受管文件的落点映射.

v3 的 ``validate_path`` / 工作目录边界已经退役：路径能不能写由 ``zones.py`` 按分区判定，
任务边界由会话沙箱（``cli.py open --workspace``）判定。两层各管一件事，不再在
project_paths 里重叠。

这里只剩「落点规则要用的映射表」—— 它是 Assets/Mine 的现实布局，不是权限。
"""

import os

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))

# 类别 → 基础目录 —— 落点检查用它校验 `category` 注解与路径是否一致。
CATEGORY_BASE: dict[str, str] = {
    "PostProcess": "Assets/Mine/Shaders/PostProcess",
    "Render":      "Assets/Mine/Shaders/Render",
    "Graph":       "Assets/Mine/Shaders/Graph",
    "Script":      "Assets/Mine/Scripts",
}

# 受管文件的顶层根。
#
# 为什么只到「顶层根」这一层：现实里 Shaders/<Cat>/<Effect>/ 之外，Scripts/<Module>/
# 还带 Editor/ Shaders/ Noises/ Water/ 等嵌套子目录，Scripts/TestAuto.cs 更是直接躺在
# 根下。任何按深度或「必须进效果子目录」的规则，第一次运行就会误拦既有文件。
# 本层只拦「扔在根下或扔进非代码区」这一类真实错误。
MANAGED_ROOTS = ("Effects", "Scripts", "Shaders", "Special")
