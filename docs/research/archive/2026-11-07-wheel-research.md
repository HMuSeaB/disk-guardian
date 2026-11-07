# 图形化路线与社区轮子调研 (历史归档)

> [!WARNING]
> **版本废弃与结论修正声明**：
> 1. 本文档第 1 节关于“Dism++ 主程序闭源导致规则编译进 exe/dll 无法魔改与提取”的推论，**已在后续的黑盒解包中被完全推翻**。实测在 `Config\Data.zip` 中已完整发现明文 `Data.xml`（448KB，71条规则）。
> 2. 最新取证与整合结论请转至规范文档：[`docs/research/dism-reverse-and-integration.md`](../dism-reverse-and-integration.md)。
> 3. 本文档仅作为历史技术调研记录归档，保留其对 `disk-butler` 等开源轮子的横向对比分析价值。

---

## 一、「魔改 Dism++」早期判断（已被后续取证纠偏）

此前探讨 Dism++ 开源情况时发现：
- `GET api.github.com/repos/Chuyu-Team/Dism++` 报 404，组织仓库内无主程序源码。
- 当时推论其规则与引擎强绑定在二进制内。后续已通过解包证明 `Data.xml` 为明文，详见最新整合方案。

---

## 二、GitHub 社区轮子横向调研

### 现成轮子（有源码、许可可用）

| 项目 | 技术栈 | Stars / 许可 | 核心定位 |
| :--- | :--- | :--- | :--- |
| `darkmatter2048/WindowsCleaner` | TypeScript | 5.0k / 无明确许可 | 中文 C 盘清理，轻量免安装 |
| `YGTQ3/disk-butler` | **Tauri 2 + Rust + React** | 650 / MIT | C 盘管家：磁盘透视、一键清理、启动管理 |
| `iUnreallx/Quanta-Cleaner` | C++17 + Qt6/QML | MIT | 分类环 + 9 类清理 |
| `demigodmode/DiskSweeperPro` | PySide6 + Rust CLI | Apache-2.0 | Safe/Moderate/Aggressive 分级清理 |

### `disk-butler` 源码级拆解证据

下载并逐行核对 `disk-butler` 源码发现：
1. **白名单设计一致**：`cleanup.rs:1-3` 明确由前端仅回传条目 id，实际路径在后端重算，与本项目的安全原则一致。
2. **致命 API 隐患**：`cleanup.rs:127-130` 采用了 `SHEmptyRecycleBinW`。实测在 Windows 存在损坏文件时该 API 会弹出系统模态框卡死，此缺陷在本项目中通过按 `$I`/`$R` 手工批次清理得到解决。
3. **删除语义激进**：`delete_contents()` 直接调用 `remove_dir_all` 实施物理删除，未提供可撤销的两步机制。
4. **缺乏硬禁区概念**：全仓未对 `winevt`、`assembly`、`MEMORY.DMP` 等关键排障证据进行强阻断，甚至将转储文件标记为可清理。

---

## 三、技术选型推演结论

经综合评估，本项目选择 **Python 引擎 + 本地回环 WebUI 方案**（无边框 `--app=` 窗口）：
- CLI 与 GUI 共享单一事实来源 `guardian.execute_rules`；
- 零额外环境依赖（依托本机托管 Python）；
- 完全落实一次性令牌、回环绑定与安全审计契约。

