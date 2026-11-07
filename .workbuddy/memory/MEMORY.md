# disk-guardian 项目长期记忆

## 归集与定位

- 源码在这里：`D:\4rchive\Code\disk-guardian\`（独立 git 仓库）。
- 双击入口在 `C:\Users\36230\SystemTools\disk-guardian\`，已登记在
  `C:\Users\36230\SystemTools\索引.md` **第一节**。
- 判据是用户 2026-10-09 定的那句「双击它，本机有没有被改动」：本工具删本机文件 → 归「一」。
  源码放 `D:\4rchive\Code\` 是 wintuner 模式，不是「三」（不改远端账号）。
- 定位：**Dism++ 的补集，不是替代品**。Dism++ 管 WinSxS / 事件日志 / 还原点；
  本工具管 shader / 包管理器缓存 / 构建产物。

## 图形界面（2026-11-07 v0）

- 用户要「像 Dism++ 一样的图形化、现代化」。做了 `ui/server.py`（标准库
  `http.server`，仅回环、一次性令牌、首页也校验、`--app=` 窗口、**不自动退出**）
  + `ui/assets/index.html`（单文件、离线、深浅主题、四个页面），双击入口
  `SystemTools\disk-guardian\5-打开图形界面.bat`（用 `pythonw.exe`）。
- **魔改 Dism++ 不可行**：`api.github.com/repos/Chuyu-Team/Dism++` 是 404，
  组织 24 个仓库里没有主程序源码，开源的只有 `Data.xml` / 语言包 / 插件文档 /
  周边 C++ 库，主程序是闭源单 exe。中文推广站写「MIT 开源」不可信。
  （纠正此前「Dism++ 开源」的说法。）
- 同类轮子源码级核对见 `docs/调研-2026-11-07.md`：`disk-butler`
  （Tauri2+Rust，650 stars）已做白名单制 / 只读报告 / 单测，但**用了
  `SHEmptyRecycleBinW`**、永久删、没有 `winevt`/`assembly` 禁区概念。
  我们的差异化是「事故取证结论 → 不可绕过的禁区 + 两步删除 + 动作流水」。
- CLI 与 GUI **共用 `guardian.execute_rules`**，前端只传 id；后端用刚重建的
  plan 复核等级/状态/禁区。别复制第二份删除逻辑。
- v0 依赖本机托管 Python，不是单 exe；形态确认后可再用 wintuner 的
  Go + 内嵌网页骨架包一层。

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
