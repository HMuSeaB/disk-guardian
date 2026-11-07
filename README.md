# disk-guardian

> 规则驱动、预览优先的磁盘清理工具。**只补 Dism++ 不覆盖的那一部分，不替代 Dism++。**
>
> 源码：`D:\4rchive\Code\disk-guardian\`（独立 git 仓库）
> 双击入口：`C:\Users\36230\SystemTools\disk-guardian\`（登记在 `SystemTools\索引.md` 第一节）

---

## 它解决什么

2026-11-07 那天 C 盘和 D 盘同时冒红，Dism++ 清了一轮几乎没变化，反而越清越紧。
复盘出来的几件事，全部固化进了这个项目：

| 当天实测 | 原因 | 项目里的做法 |
|---|---|---|
| Dism++ 清完只省了零头 | 它管 `WinSxS` / 事件日志 / 还原点，**没有** shader 缓存、包管理器缓存、构建产物规则 | `rules.json` 专收 Dism++ 的空白：`NVIDIA\DXCache` 8.25 GiB、`go-build`、npm/pip/pnpm/cargo/go-mod/gradle/nuget 缓存 |
| 清完三小时 C 盘又少 4.27 GiB | 勾了「NET 程序集缓存」，NGEN 后台重建 `C:\Windows\assembly\NativeImages_*` 约 2.5 GiB | `%SystemRoot%\assembly` 列入 `HARD_DENY`，代码级禁止 |
| 11/4–11/6 蓝屏事件在系统里查不到了 | 勾了「Windows事件」，`System` / `Application` / `Setup` / `WindowsUpdateClient Operational` 被清空 | `%SystemRoot%\winevt` 列入 `HARD_DENY`；证据只在报告文件里留档 |
| 清空回收站时弹出 `$R02DUPK` / `0x80004005` 卡死 | `SHEmptyRecycleBinW` 遇到异常项会弹模态框，`SHERB_NOCONFIRMATION` 也挡不住 | `recycle.py` 完全不用该 API，改按 `$I`/`$R` 对逐批删，失败项跳过 |
| 清理脚本卡住后日志全丢 | 日志写在脚本末尾，没走到就没落盘 | `guardian.py` 每行 `flush + fsync`，另有 `out/actions.jsonl` 每条动作即时追加 |
| 一堆一次性 Python 扫描脚本散在会话工作区 | 每次现写、路径硬编码 | 全部收成 `rules.json` + 一个 `guardian.py` |

---

## 三条铁律

1. **默认只读。** 直接跑 `python guardian.py` 只扫、只打印，不删任何东西（只在项目 `out/` 里写日志）。
   必须显式加 `--apply`，并且交互输入 `DELETE`（level 2/3 是 `DELETE2`/`DELETE3`）才动手。
2. **禁区写在代码里。** `HARD_DENY` 是 `guardian.py` 的常量，**不在 `rules.json` 里**。
   规则表改坏、路径写宽、变量拼错，都打不开禁区。启动时还有一轮 `deny_selftest` fail-fast，
   禁区出现漏洞就直接拒绝运行。
3. **不调用 `SHEmptyRecycleBinW`。** 清空回收站走 `recycle.empty_safely()`：按批删，
   失败批次拆成单文件重试，坏项跳过，全程无 UI、不会挂住。

**⚠️ 此操作非常危险，可能导致不可逆的数据丢失！**
`--apply` 只是把文件**移入回收站**（可撤销）；`--purge` 才是永久删除，会再列一次清单并要求输入 `PURGE`。
两步分开，绝不一次做完。

---

## 快速开始

### 双击（推荐给日常用）

`C:\Users\36230\SystemTools\disk-guardian\` 里四个 `.bat`，按顺序：

| 文件 | 做什么 | 破坏性 |
|---|---|---|
| `1-预览可清理.bat` | 只读预览 level 1 + 生成报告 | 无 |
| `2-执行安全清理.bat` | level 1 移入回收站，需输入 `DELETE` | 可撤销 |
| `3-清空回收站.bat` | 永久清空 C 盘回收站，需输入 `PURGE` | **不可撤销** |
| `4-打开报告目录.bat` | 打开 `out\` 看日志和报告 | 无 |
| `5-打开图形界面.bat` | 启动现代化窗口界面（见下节） | 界面本身只读 |

### 命令行

```bat
cd /d D:\4rchive\Code\disk-guardian
set PY=C:\Users\36230\.workbuddy\binaries\python\versions\3.13.12\python.exe

%PY% guardian.py --list                        :: 列出所有规则 id 与等级
%PY% guardian.py --check-rules                 :: 硬禁区自测 + 规则校验
%PY% guardian.py                               :: 只读预览 level 1
%PY% guardian.py --level 2 --json-report       :: 预览 level 1+2
%PY% guardian.py --level 3 --json-report       :: 预览全部（仍然只读）
%PY% guardian.py --apply                       :: 执行 level 1（移入回收站）
%PY% guardian.py --apply --level 2             :: 执行 1+2，需输入 DELETE2
%PY% guardian.py --include npm-cache,pip-cache --level 2 --apply
%PY% guardian.py --purge                       :: 永久清空回收站，需输入 PURGE
%PY% guardian.py --purge --drives C,D          :: 连 D 盘回收站一起（会额外列清单）
```

`--yes` 跳过交互确认，只给脚本化运行用；非交互终端下不带 `--yes` 会直接拒绝。

---

## 图形界面（v0，2026-11-07 新增）

双击 `5-打开图形界面.bat`：用 `pythonw.exe` 起一个**只监听 127.0.0.1** 的本地服务，
再用系统 Edge/Chrome 的 `--app=` 打开无地址栏窗口，看起来就是个本地程序。

界面四个页面：**清理规则**（C/D 水位、L1/L2/L3 切换、规则卡片、勾选、二次确认弹窗）、
**硬禁区**（35 条受保护路径 + 为什么）、**回收站**（清单 + `PURGE` 确认）、
**运行日志**（日志末尾 + `actions.jsonl` 动作流水）。深色/浅色主题可切。

安全边界与命令行完全一致，不是另写一套：

- GUI **只传规则 id，从不传路径**；后端用刚重建的 plan 复核等级 / 状态 / 禁区，
  再调 `guardian.execute_rules` —— CLI 和 GUI 共用同一条执行路径。
- 首页 `?t=` 校验一次性令牌，API 走 `X-DG-Token`；非回环地址直接拒绝。
- 执行弹窗要求输入 `DELETE` / `DELETE2` / `DELETE3`，服务端**再校验一次**；
  清空回收站要求 `PURGE`，同样双重校验。
- 默认**不自动退出**（后台标签页会被浏览器冻结，心跳会误判），
  退出只能点界面右上角按钮 → `POST /api/quit`。
- 双击没反应时看 `out/gui_crash.txt`（pythonw 没有控制台，所以异常写成文件）。

完整调研见 `docs/调研-2026-11-07.md`，一句话结论：
**「魔改 Dism++」做不到**（GitHub 上只有 `Data.xml` / 语言包 / 插件文档，
主程序是闭源二进制）；同类通用清理器很多，但把一次真实排障结论固化成
**不可绕过的禁区 + 两步删除 + 动作流水**的，确实没找到第二家。

> 这是验证形态的 v0：依赖本机托管 Python，不是单 exe。
> 形态确认后可以再用 wintuner 那套 Go + 内嵌网页骨架包成单文件分发。

**扫描耗时（本机实测）**：L1 约 0.02 s；L2 约 12 s（30 万个文件）；
L3 约 15 s（48 万个文件）。切等级时前端显示加载态，服务端缓存 5 分钟，
执行前一定用 `refresh=True` 重新扫一遍，不会照着旧计划动手。
有一个反直觉的实测：**给扫描加 4 线程池反而更慢**（L2 11.2 → 11.9 s，
L3 13.9 → 17.4 s，单块 SSD 上 `scandir` 并发只是争用），所以保持串行；
真正的提速来自去掉「每个文件重复一次 `os.stat`」。后续想再快，
应该做「按规则粒度的增量缓存」，而不是加线程。

---

## 分级

| level | 含义 | 怎么才会进计划 |
|---|---|---|
| 1 | 纯缓存，系统/工具自动重建 | 默认 |
| 2 | 能重建但要联网重下，或需要管理员 | 必须 `--level 2` 或更高 |
| 3 | 会永久丢数据（镜像/卷/SDK/用户文件） | 必须显式 `--level 3` |
| 0 | 禁区 | 不存在于规则表，由 `HARD_DENY` 硬编码 |

另外三道闸门：`requires_admin` 未提权就跳过；`recent_hours`（如 WU 下载缓存设 6 小时）
近期有写入就跳过；`mode: old-files` 的规则不删目录本身，只挑 `keep_days` 天前的旧文件。

---

## 硬禁区（节选，完整表在 `guardian.py`）

整棵子树禁止：`%SystemRoot%\assembly`、`WinSxS`、`Installer`、`winevt`、`System32`、
`SysWOW64`、`Boot`、`Fonts`、`MEMORY.DMP`、`Minidump`、`%ProgramFiles%`、
`%ProgramFiles(x86)%`、`Documents`、`Desktop`、`Pictures`、`.workbuddy`、`.ssh`、`.gnupg`、
`%LOCALAPPDATA%\CrashDumps`、`...\Edge\User Data`、`C:\$Recycle.Bin`、`C:\Windows.old`。

自身禁止、且**不允许任何目标作为它们的父目录**：`%USERPROFILE%`、`%LOCALAPPDATA%`、
`%APPDATA%`、`%SystemRoot%`、`%ProgramData%`、`%CODE_ROOT%`、盘符根目录、
`pagefile.sys` / `swapfile.sys` / `hiberfil.sys` / `dedicateddumpfile.sys` / `MEMORY.DMP` / `NTUSER.DAT`。

> 例外只有两个，且都要求 `level 3` + 规则里显式 `allow_inside_deny: true`：
> `edge-cache`（只碰 `User Data\Default` 下三个缓存子目录）、
> `workbuddy-old-logs`（只挑 `logs` / `traces` 里 14 天前的旧文件）。

---

## 改规则表

新增一条缓存规则，**只改 `rules.json`**，不用碰 Python：

```json
{
  "id": "my-tool-cache",
  "name": "某某工具缓存",
  "level": 1,
  "paths": ["%LOCALAPPDATA%\\MyTool\\Cache"],
  "rebuild": "下次启动自动重建",
  "note": "为什么安全"
}
```

可用变量：`%USERPROFILE%` `%LOCALAPPDATA%` `%APPDATA%` `%TEMP%` `%SystemRoot%`
`%ProgramData%` `%ProgramFiles%` `%ProgramFiles(x86)%`，以及 `vars` 里的 `%CODE_ROOT%`。
拼错变量会在 `load_rules()` 里直接报错退出，不会静默变成别的路径。

**想改禁区必须改 `guardian.py` 里的常量，并且重跑 `--check-rules` 看自测是否全 PASS。**
这是故意的：规则表是可编辑数据，禁区是不可编辑代码。

---

## 日志与报告

| 文件 | 内容 |
|---|---|
| `out/run_YYYYMMDD_HHMMSS.log` | 每次运行逐行落盘，每行 flush + fsync |
| `out/actions.jsonl` | 每条实际删除动作（规则、路径、ok/failed），追加即落盘 |
| `out/last_report.json` | 传 `--json-report` 时生成，含每条规则的大小/文件数/状态/拦截原因 |

`out/` 已在 `.gitignore` 里排除，不入库。

---

## 与 Dism++ 的分工

| 谁管 | 内容 |
|---|---|
| **Dism++** | 被取代的 WinSxS 组件、系统还原点、Windows 事件、更新安装记录、Package Cache、NET 程序集缓存（都不建议随便清） |
| **disk-guardian** | shader/DXCache、GLCache、go-build、npm/pip/pnpm/cargo/go-mod/gradle/nuget/ms-playwright 缓存、Rust `target`、各类临时目录 |
| **都不碰** | 已装程序、用户文档、Docker/WSL 虚拟盘里的数据、pagefile/dump 文件 |

Dism++ 本机配置建议（观察期）：
`Config\Config.ini` 里 `#系统以及程序崩溃dmp文件（by YukiSakura）=1` 建议改 0，
别在 25H2 蓝屏观察期把 dmp 清掉。其余危险项本机当前已经是 0。

---

## 已知不做

- 不自动跑、不设计划任务、不联网、不上传任何统计。
- 不动卷影副本 / 还原点（`vssadmin` 需要管理员，另说）。
- 不删 `C:\Windows\assembly\NativeImages_*`：清了会触发 NGEN 重建反吃空间。
- 不用 `SHEmptyRecycleBinW`，不用 `rm -rf` 一类整目录强删。
- 不理解「仓库有没有推完整」——那是另一份 `清理清单_2026-11-07.md` 的事；
  删构建产物前请自行确认远端已有完整副本。

---

## 项目结构

```
D:\4rchive\Code\disk-guardian\
  guardian.py              主程序：扫描 / 预览 / 执行 / 硬禁区 / 日志
  rules.json               规则表（26 条）与分级说明
  recycle.py               回收站封装（规避模态弹窗）
  ui\server.py             回环 WebUI 服务（令牌 / API / --app= 窗口）
  ui\assets\index.html     单文件现代化界面（无外部资源，离线可用）
  docs\调研-2026-11-07.md  魔改可行性与同类项目源码级调研
  README.md                本文件
  .gitignore / .gitattributes
  tools\make_launchers.py  生成 5 个 .bat 入口，保证 GB18030 + CRLF
  launchers\               生成的 .bat 副本（仓库自留）
  out\                     日志与报告（不入库）

C:\Users\36230\SystemTools\disk-guardian\
  1-预览可清理.bat / 2-执行安全清理.bat / 3-清空回收站.bat /
  4-打开报告目录.bat / 5-打开图形界面.bat
```

`.bat` 约定（见 `SystemTools\索引.md` 第四节）：**GB18030 + CRLF + 无 BOM + `chcp 936`**，
文件名不含半角括号，标签用 ASCII，分支走 `goto` 不用 `if (...)` 块。
生成器还固定了 `PYTHONUTF8=1` / `PYTHONIOENCODING=utf-8`：本机 ACP 是 936，
而这个托管 Python 开了 UTF-8 模式；真实控制台走 `WriteConsoleW` 不受代码页影响，
但显式固定后管道 / 重定向 / 日志三者的编码一致，不会有歧义。
**不要手工编辑 `.bat`**，改模板后跑 `tools\make_launchers.py` 重新生成。
