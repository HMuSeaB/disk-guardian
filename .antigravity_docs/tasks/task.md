# 任务看板：ExplorerDoctor 资源管理器与系统外壳健康体检系统

## 当前任务目标
为 `DiskGuardian` 构建系统级外壳医生 `ExplorerDoctor`，提供对 Windows 11 原生标签页资源管理器的健康诊断、降级原因探测与一键修复能力，与 `ProxyDoctor` 并列作为核心守护模块。

## 进度看板
- [x] **Core 诊断引擎研发**:
  - [x] 创建 `src/DiskGuardian.Core/Diagnostics/ExplorerDoctor.cs`；
  - [x] 实现注册表 `Shell Extensions\Blocked` 探测与修复；
  - [x] 实现 `Advanced\SeparateProcess` 探测与一键归零；
  - [x] 实现提权与环境风险取证分析；
  - [x] 注入 Win32 API `WM_SETTINGCHANGE` 广播通知（严禁杀死 explorer）。
- [x] **CLI 命令行扩展**:
  - [x] 支持 `--check-shell` 与 `--fix-shell` 命令行参数及美化控制台输出。
- [x] **UI 仪表盘集成**:
  - [x] 在 `DashboardPage.xaml` 中设计高质感 ExplorerDoctor 诊断卡片；
  - [x] 呈现 4 宫格指标、状态徽章与底层取证折叠面板；
  - [x] 绑定一键体检与一键安全修复交互。
- [x] **工程验证**:
  - [x] 全套构建编译 0 警告 0 错误；
  - [x] CLI 真实命令执行验证；
  - [x] 确保桌面安全规则零违规（保护 explorer.exe 与 DeskBox 微件）。
- [x] **文档与双语门面更新**:
  - [x] 同步更新 `README.md` 与 `README_EN.md`。
