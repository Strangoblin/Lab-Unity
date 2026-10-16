# DiffuseGI / AO 清理与历史采集证据

背景见 `2026-09-21-opencode-era-ledger.md`、`2026-09-21-ssgi-phase4-ao.md`。
旧 rollback tar 与 cleanup manifest 原字节保留。原清单标题写“未执行”，实际 98 项删除状态由台账说明。
这是已有、memory 引用的历史回退点例外；不新增 tar 包，也不展开整包成为可运行临时项目。
archive-members.json 列出 454 个成员，含 207 个普通内容文件与 227 个 AppleDouble；均无越界路径。

`historical-source/verify3.cs` 和 verify4.cs 从回退包实际读取，补回正式 AO 文档所引用的缺失脚本。
是 2026-09-21 的 URP / AO 当时版本探针，后续时域、falloff、半径与 Resolve 已改变；禁止宣称适配当前 AO。
19 个非重复历史截图保留原编码；duplicate-aliases.json 记录已提议删除的同字节路径别名。
图片为 642×522 离屏相机；hb/ss 是 HBAO/SSAO，visibility 是可见度，composite 是合成。
verify3.cs 明确对 raw_* 六组配置关闭后处理；其它旧探针不以同一状态推定，PNG 亦非线性 HDR。
可见度图仍有后续透明水面，不能把蓝色区域当作 AO 值；正式历史统计边界见当日 memory。
不同编码即使看起来相近也保留，未按相似度删除。
历史数据不是当前实现的通过证明；当前 AO 需要按正式 README 重新采集。

tar 的内容校验仅作历史可读取性确认，没有在生产项目恢复或运行其中脚本。
