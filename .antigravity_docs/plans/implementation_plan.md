# 实施计划 - 系统网络与幽灵代理诊断修复模块 (ProxyDoctor)

本文档规划将 Windows 幽灵系统代理（Ghost Proxy）诊断、死端口探测、二进制注册表清洗与一键修复能力集成至 `disk-guardian`。

---

## 1. 架构与模块设计

### 1.1 核心层：`DiskGuardian.Core/Diagnostics/ProxyDoctor.cs`
- **诊断 (`Diagnose()`)**:
  - 读取 `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings`:
    - `ProxyEnable` (DWORD)
    - `ProxyServer` (String)
  - 读取 `Connections` 下的二进制键:
    - `DefaultConnectionSettings` (Binary)
    - `SavedLegacySettings` (Binary)
  - **冲突判定 (Inconsistent)**:
    - `ProxyEnable == 0`，但二进制第 8 字节 Flags 依然含有 `0x02` (手动代理启用标志)；
  - **死代理探测 (Dead Port Probe)**:
    - 若配置了回环地址（如 `127.0.0.1:7890`），发起低超时 (300ms) TCP 连接探测；若端口未监听，判定为死代理；
  - **环境变量检测**:
    - 检查 `EnvironmentVariableTarget.User` 下的 `HTTP_PROXY` / `HTTPS_PROXY` / `ALL_PROXY` 是否残留不可达地址；
  - **下游工具弱点检测**:
    - 检查 `~/.workbuddy/settings.json` 是否缺失 `"mode": "none"`。

- **修复 (`Repair(bool hardenWorkBuddy = true)`)**:
  - **自动备份**: 导出注册表原值及二进制数据至 `out/proxy_backup_<timestamp>.reg`；
  - **清除死代理与修正二进制**:
    - 将 `ProxyEnable` 改为 `0`；
    - 将 `DefaultConnectionSettings` 与 `SavedLegacySettings` 第 8 字节置为 `0x01`（直连），剔除 `0x02`；
  - **WinINet 原生广播刷新**:
    - 调用 `InternetSetOptionW(INTERNET_OPTION_SETTINGS_CHANGED)` 与 `INTERNET_OPTION_REFRESH`；
    - 严禁重启 `explorer.exe`；
  - **环境变量与 WorkBuddy 加固**:
    - 清理失效的用户级代理环境变量；
    - 向 `~/.workbuddy/settings.json` 写入 `"mode": "none"` 建立坚固屏障。

---

### 1.2 命令行工具：`DiskGuardian.Cli`
- 支持 `--check-proxy`：输出当前网络与代理体检报告（存在幽灵代理时退出码 1，健康时退出码 0）；
- 支持 `--fix-proxy`：自动备份并一键执行安全修复与广播刷新。

---

### 1.3 桌面端：`DiskGuardian.UI` (WinUI 3 Fluent)
- 在 `DashboardPage.xaml` 新增【系统网络与幽灵代理体检】Fluent 卡片：
  - 实时展示健康指标、当前代理配置、监听状态；
  - 若存在异常（如 `127.0.0.1:7890` 死代理残留），显示警示与详细提示；
  - 提供【一键安全修复】按钮，点击后立即无痛恢复直连并刷新状态。

---

## 2. 计划与检查清单

- [ ] 1. 编写 `ProxyDoctor.cs` 并加入 `DiskGuardian.Core`
- [ ] 2. 在 `DiskGuardian.Cli` 接入 `--check-proxy` 与 `--fix-proxy` 命令
- [ ] 3. 在 `DashboardPage` 引入代理体检卡片与交互事件
- [ ] 4. 本地自动化断言测试与验证
- [ ] 5. 提交代码至 GitHub 触发 Actions CI
