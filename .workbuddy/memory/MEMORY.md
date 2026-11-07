# disk-guardian 项目长期记忆

## 归集与定位

- 源码在这里：`D:\4rchive\Code\disk-guardian\`（独立 git 仓库）。
- 双击入口在 `C:\Users\36230\SystemTools\disk-guardian\`，已登记在
  `C:\Users\36230\SystemTools\索引.md` **第一节**。
- 判据是用户 2026-10-09 定的那句「双击它，本机有没有被改动」：本工具删本机文件 → 归「一」。
  源码放 `D:\4rchive\Code\` 是 wintuner 模式，不是「三」（不改远端账号）。
- 定位：**Dism++ 的补集，不是替代品**。Dism++ 管 WinSxS / 事件日志 / 还原点；
  本工具管 shader / 包管理器缓存 / 构建产物。

## 不可动摇的设计约束

1. 默认只读预览。必须 `--apply` + 交互令牌（`DELETE` / `DELETE2` / `DELETE3`）。
2. `HARD_DENY` 写在 `guardian.py` 常量里，**绝不能搬进 `rules.json`**。
   规则表是可编辑数据，禁区必须是可审查的代码；同时要拦「目标作为受保护路径的父目录」。
   改完必须跑 `guardian.py --check-rules`，18 条断言全 PASS 才算数。
3. **绝不调用 `SHEmptyRecycleBinW`**：2026-11-07 实测遇到 `$R02DUPK` / `0x80004005`
   会弹模态框卡死，`SHERB_NOCONFIRMATION` 也挡不住。清空走 `recycle.empty_safely()`。
4. `--apply` 只移入回收站（可撤销），`--purge` 才永久删（令牌 `PURGE`），两步分开。
5. 日志每行 flush + fsync；动作另外写 `out/actions.jsonl`。
   原因是 `stage1_clean.py` 曾卡住后日志一个字都没落盘。

## 本机实测结论（2026-11-07）

- 清「NET程序集缓存」→ NGEN 后台重建 `NativeImages_*` 反吃约 2.5 GiB，别碰。
- 清「Windows事件」→ 11/4–11/6 蓝屏事件证据在系统里被抹掉，别碰。
- `NVIDIA\DXCache` 曾 8.25 GiB、`go-build` 1.08 GiB，Dism++ 完全没有对应规则。
- 第一档清理后 C free 从 29.56 → 39.87 GiB（约 +10.31 GiB）。
- 25H2 蓝屏观察期：别勾 Dism++ 的「被取代的 WinSxS 组件」「系统还原点」「Windows事件」；
  本机 `Config.ini` 里 `#系统以及程序崩溃dmp文件=1` 建议改 0。

## 编码 / 打包约定

- `.bat` 由 `tools\make_launchers.py` 生成，**不要手工编辑**。
  GB18030 + CRLF + 无 BOM + `chcp 936`；文件名不含半角括号；ASCII 标签；
  不用 `if (...)` 块（会闪退）；显式 `PYTHONUTF8=1` / `PYTHONIOENCODING=utf-8`
  （本机 ACP=936 但托管 Python 是 UTF-8 模式）。
- `.gitattributes` 里 `*.bat text eol=crlf` 必须写在 `* text=auto eol=lf` **之后**。
- `out/` 不入库。没有配远端，推之前先确认路径里没有本机敏感信息。
