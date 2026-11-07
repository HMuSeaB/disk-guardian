# 蓝屏排障事实与禁区溯源台账 (2026-11-04 ~ 11-06)

> **核心定位**：本文件是 `disk-guardian` 规则体系的“事实出处（Provenance）”。
> 本项目的首要目标不是激进腾出空间，而是**将真实事故的惨痛教训固化为不可绕过的安全契约**。
> 没有本台账，`guardian.py` 中的 `HARD_DENY` 将失去其物理合法性。

---

## 1. 事故背景与物理环境

- **测试机型**：来酷 Bellator N176 笔记本
- **硬件配置**：
  - CPU: AMD Ryzen 9 8940HX
  - 显卡: NVIDIA GeForce RTX 5060 Laptop GPU + AMD Radeon 610M (核显)
  - 内存: 32 GB (Micron 16G + Sinker 16G 混插，DDR5-5200 JEDEC，未超频)
- **操作系统**：Windows 11 26100.7462 (24H2/25H2 预览分支)，开启 VBS-HVCI (基于虚拟化的安全性)
- **发生时间**：2026-11-04 至 2026-11-06
- **排障现场与工具仓库**：
  - 排障工具链与完整 15 节取证长文归存于：`D:\4rchive\Code\DOUZHANZHE-Control\tools\crash\`
  - 符号级取证主报告：`D:\4rchive\Code\DOUZHANZHE-Control\tools\crash\crash-report-2026-11-04.md`
  - 使用 WinDbg 官方 `cdb.exe` 配合微软符号服务器对 minidump 逐个进行 `!analyze -v` 符号级还原。

---

## 2. 事故核心结论与教训

1. **BugCheck 集中显形**：
   - 发生多次 `0x1E (KMODE_EXCEPTION_NOT_HANDLED)`、`0xA (IRQL_NOT_LESS_OR_EQUAL)`、`0x50 (PAGE_FAULT_IN_NONPAGED_AREA)`、`0xBE (ATTEMPTED_WRITE_TO_READONLY_MEMORY)` 与 `0x4D (PFN_LIST_CORRUPT)`。
   - 核心调用栈崩在 `nt!ExpGetPoolTagInfoTarget+0x1c5`，系内核在 CPU 空闲循环（`KiIdleLoop`）中遍历池标签时读到非法指针 `0xFFFFFFFFFFFFFFFF`。
2. **证据链断裂惨痛教训**：
   - 11-04 当天至少发生 6 次崩溃，但在排障时发现早期的 `110426-8703-01.dmp` 与部分关键事件日志已被系统或传统清理机制擦除，导致定位过程一度陷入被动。
3. **专用防御机制落地**：
   - 为确保蓝屏即使在系统盘极度紧张时也能完整落盘，系统配置了 `dedicateddumpfile.sys` 独立转储文件。
   - 启用了驱动验证器（Driver Verifier）特殊池保护，系统目前处于 **25H2 蓝屏观察期**。

---

## 3. 事故教训与 disk-guardian 硬禁区 (HARD_DENY) 映射矩阵

`disk-guardian` 绝不使用通用的“推荐清理”逻辑。本工具所有的 `HARD_DENY` 代码常量，均直接由本次排障事实一对一派生：

| 受保护路径 / 对象 | 来源事故教训 | 为何绝对禁止清理 (HARD_DENY) | 对应工具对比 |
| :--- | :--- | :--- | :--- |
| `%SystemRoot%\System32\winevt\Logs` | 11-04 蓝屏事件证据断链 | Windows 事件日志记录了 BugCheck 发生前后的驱动状态与硬件错误（WHEA），一旦清空将彻底丧失根因定位能力。 | Dism++: L3（允许清）<br/>**disk-guardian: HARD_DENY** |
| `%SystemRoot%\MEMORY.DMP`<br/>`%SystemRoot%\Minidump`<br/>`%LOCALAPPDATA%\CrashDumps` | 5 次内核崩溃转储排障 | 当前正处于 25H2 观察期，保留 dmp 是判断内存混插与驱动验证器是否再次捕获野指针的唯一物证。 | 通用清理器: 默认列为垃圾<br/>**disk-guardian: HARD_DENY** |
| `*\dedicateddumpfile.sys` | 专用转储保障机制 | 本机通过脚本专门分配的应急转储文件，删除将导致系统蓝屏时无法生成崩溃转储。 | **disk-guardian: HARD_DENY** |
| `%SystemRoot%\Microsoft.NET\assembly`<br/>`%SystemRoot%\assembly` | 11-07 磁盘清理实测事故 | 清空 `NativeImages_*` 缓存后，系统后台 NGEN 服务会立即并发满载重编译，反向吞噬约 2.5 GiB 磁盘，并造成系统卡顿与干扰。 | Dism++: L2（推荐清）<br/>**disk-guardian: HARD_DENY** |
| `%SystemRoot%\Installer` | MSI 依赖完整性 | 存放软件卸载与修复基准包，清空会导致后续软件升级、卸载甚至系统补丁修复全面报错。 | Dism++: L0（带警告）<br/>**disk-guardian: HARD_DENY** |
| `%SystemRoot%\WinSxS`<br/>系统还原点 | 观察期系统回滚能力 | Dism++ 的“被取代组件清理”与“ResetBase”在 25H2 观察期极度危险，一旦更新引入新崩溃，清空后将无法卸载更新。 | Dism++: L2（推荐清）<br/>**disk-guardian: 观察期全面冻结** |

---

## 4. 机器状态契约 (Machine State Context)

根据 `docs/architecture/evidence-ledger-design.md` 的规范，策略编译必须输入当前机器状态：

```text
       [机器状态: OBSERVATION (观察期)]
                     │
    ┌────────────────┴────────────────┐
    ▼                                 ▼
【永久禁区】                       【观察期专属禁区】
• winevt 事件日志                 • 崩溃转储 (dmp)
• dedicateddumpfile.sys           • WinSxS 组件存储清理
• NET 程序集缓存 (assembly)       • 系统还原点
• Installer 目录
```

- **当前生效状态**：`OBSERVATION`（自 2026-11-04 起生效）。
- **解除条件**：连续 30 天无 BugCheck 记录，且经人工审核后方可将机器状态迁至 `NORMAL`；即使进入 `NORMAL`，事件日志与专用转储仍受保护。

