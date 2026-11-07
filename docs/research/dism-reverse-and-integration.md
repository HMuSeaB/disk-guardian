# Dism++ 黑盒取证与规则整合方案

> **状态**：有效 / 阶段 0 台账输入素材  
> **数据产物**：`docs/data/dism-rules-inventory.json`（71 条规则结构化盘点）  
> **复验脚本**：`out/research/analyze_dism.py`、`out/research/pe_probe.py`

---

## 1. 执行摘要与核心结论 (Executive Summary)

针对 Dism++ 的整合策略：**不做整体完整复刻；提取其规则明文作为输入，喂入我们自己的安全规则引擎**。

### 核心事实
1. **主程序闭源，但规则随程序明文分发**：本机 `Config\Data.zip` 内包含 448 KB 明文 UTF-8 的 `Data.xml`。CleanCollection4 共有 **71 条清理规则，其中 68 条为纯声明式 XML**，仅 3 条依赖内置 C++ 函数。
2. **现代开发者工具链完全空白**：全文检索 DXCache、Shader、npm、pnpm、cargo、go-build、gradle、NuGet、Docker、Cursor 等工具链缓存，**命中全部为 0**。这正是 `disk-guardian` 存在的核心差异化价值。
3. **Level 等级绝不能直接映射**：Dism++ 将 `系统以及程序崩溃dmp文件` 定为 L1，将 `Windows事件` 定为 L3。其等级并非安全性指标，若直接照搬会破坏系统排障基础。

### 能做 vs 不做

| 类别 | 决策 | 说明 |
| :--- | :--- | :--- |
| **能做（吸收）** | 空间回收声明式规则、系统优化注册表项、现代化图形界面 | 挑选经过安全复核的条目逐步吸收 |
| **改调公开命令** | 组件清理、还原点管理 | 放弃逆向其内部私有接口，必要时改调系统公开命令 `Dism.exe`（观察期暂不执行） |
| **坚决不做** | WIM/ESD 备份还原、CompactOS、热备份、驱动/Appx 离线注入 | 属于系统装配底层，不属于日常磁盘守护范畴 |

### 核心红线 (HARD_DENY)
Dism++ 允许清理、但本项目坚决禁止（HARD_DENY）的项：
- **Windows事件日志**（毁坏蓝屏与安全事件证据）
- **NET程序集缓存**（引发 NGEN 后台死循环重建反噬 2.5G 空间）
- **Installer目录**（导致后续 MSI 修复与卸载全面瘫痪）
- **崩溃转储 dmp**（破坏 25H2 观察期现场）
- **被取代的 WinSxS 组件 / 系统还原点**（破坏故障回滚能力）

---

## 2. 逆向拆解白盒架构

通过对本机安装包与 PE 导入导出的静态只读探测，还原 Dism++ 的真实运行时模型：

```text
Dism++x64.exe (1,071,616 B, PE32+ x64, 编译于 2021-08-28)
  ├─ 导出 62 个函数：自实现 DISM API 宿主（Dism* / Bcd* / Ibs* / WinRE*）
  ├─ 静态导入：COMCTL32 / KERNEL32 / msvcrt / ntdll
  │    （动态加载：CBSHost.dll、WIMGAPI.DLL、SETUPAPI.dll、VSSAPI.dll）
  └─ 规则引擎：解释 Config\Data.zip -> Data.xml
        ├─ Data.xml           71 条清理规则 + 21 组系统优化 + ESD 密钥 / WSUS / 时区表
        ├─ default.ui.zip     15 个页面 XML (Duilib 体系)
        ├─ NCleaner.dll       7 个在线清理导出函数
        ├─ CBSHost.dll        CBS 宿主调用
        ├─ wimgapi.dll        微软官方 WIMGAPI (60 个导出)
        └─ wofadk.sys         微软官方 WOF 过滤驱动
```

> **架构事实**：`NCleaner.dll` 反向导入主程序 `Dism++x64.exe` 的导出函数（如 `DismGetAllUsersAppx`、`DismRegOpenKeyEx`）。主 exe 本质是 API 宿主与规则解释器，DLL 是清理执行器。

---

## 3. 71 条规则盘点与语义冲突

盘点明细（完整明细见 `docs/data/dism-rules-inventory.json`）：
- 71 条规则按分组：缓存文件 18、临时文件 18、过期文件 14、系统相关 11、应用程序 6、备份文件 4。
- **3 个内置函数底层映射**：
  1. `NCWinEventLogOnlineCleanup` → 底层调 `EvtClearLog`。**【本项目 HARD_DENY】**
  2. `NCDOCacheOnlineCleanup` → 停止 DoSvc 服务并清理 DeliveryOptimization 缓存。**【可吸收】**
  3. `DismComponentCleanup` → 底层等价于 `Dism.exe /Online /Cleanup-Image /StartComponentCleanup`。**【观察期暂不执行】**

---

## 4. 与 disk-guardian 的对比与整合

### 4.1 冲突红线清单 (Dism++ 允许，本项目严格封死)

详见 `docs/incidents/2026-11-04-bsod-incident-and-rules.md`：
- `Windows事件` (Dism++ L3) → **HARD_DENY**
- `NET程序集缓存` (Dism++ L2) → **HARD_DENY**
- `Installer目录` (Dism++ L0) → **HARD_DENY**
- `系统以及程序崩溃dmp文件` (Dism++ L1) → **HARD_DENY**
- `WinSxS\ManifestCache` (Dism++ L3) → **HARD_DENY**

### 4.2 高价值候选吸收项 (待审条目)

| 规则名称 | 路径事实 (Data.xml) | 处置建议 |
| :--- | :--- | :--- |
| **PDB缓存文件** | `%LOCALAPPDATA%\DBG` | 纯符号缓存，编译/调试会自动重建，吸收至 Level 1 |
| **Visual Studio日志** | `%LOCALAPPDATA%\Microsoft\VisualStudio\SettingsLogs\` | 开发工具日志，吸收至 Level 1 |
| **Terminal Server Client** | `%LOCALAPPDATA%\Microsoft\Terminal` | 远程桌面缓存，吸收至 Level 1 |
| **WinINet Cookies** | `%LOCALAPPDATA%\Microsoft\Windows\INetCookies\` | 会导致登录凭据丢失，吸收至 Level 2 |
| **应用容器 Temp** | `%LOCALAPPDATA%\Packages\*\AC\Temp\` | 补充现有 `user-temp` 未覆盖的 UWP 容器临时文件 |

> **转译约束**：
> 1. Dism++ 使用 `%SystemDrive%\Users\*` 进行多用户通配，转译至 `rules.json` 时必须限定于当前用户 `%USERPROFILE%`。
> 2. Dism++ 很多顶层路径过宽（例如缩略图缓存顶层直接指向 `%LOCALAPPDATA%`），其真实过滤在 `General`/`Excluded` 节点中，**严禁仅抄录其顶层路径**。

### 4.3 差异化硬证据 (本项目独有)

对 448 KB `Data.xml` 做关键词全量检索：
- `DXCache` 0、`Shader` 0、`GLCache` 0、`node_modules` 0、`npm` 0、`pnpm` 0、`cargo` 0、`go-build` 0、`gradle` 0、`NuGet` 0、`Docker` 0、`Cursor` 0。
- **结论**：Dism++ 对现代开发环境的构建缓存完全为零。这验证了 `disk-guardian` 作为“补集”的独立存在价值。

---

## 5. 规则吸收落地建议 (三阶段推进)

- **阶段 A (格式转译)**：编写只读脚本提取 `Data.xml` 候选条目，产出标有 `source: "dism-data-xml"` 与 `reviewed: false` 的草稿。
- **阶段 B (人工核准)**：逐条审查第 4.2 节的候选条目，核定边界后加进 `rules.json`。
- **阶段 C (差异化维护)**：持续扩充工具链与 Shader 缓存规则，不因引入系统项而冲淡核心定位。

---

## 附录：本机 Dism++ `Config.ini` 当前防护状态

为防止外部清理误伤排障现场，本机配置文件已硬性调整为：
```ini
#NET程序集缓存 =0
#系统以及程序崩溃dmp文件 =0
#Package Cache目录 =0
#系统还原点 =0
#被取代的WinSxS组件 =0
#Windows事件 =0
#Windows下载缓存 =1
#Windows预读取文件 =1
#临时文件 =1
#缩略图缓存 =1
```

