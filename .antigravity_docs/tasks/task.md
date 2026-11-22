# 任务看板 (Task Board)

- [x] **任务一：BrowserDoctor 核心诊断与自愈引擎实现 (Core)** <!-- id: 1 -->
  - [x] 编写 `src/DiskGuardian.Core/Diagnostics/BrowserDoctor.cs` <!-- id: 1.1 -->
  - [x] 实现 Chromium 进程树结构与 `--type` 识别逻辑 <!-- id: 1.2 -->
  - [x] 实现安全无损重启 `gpu-process` 逻辑 (`ReleaseGpuMemory`) <!-- id: 1.3 -->
  - [x] 实现 Win32 `EmptyWorkingSet` 内存修剪逻辑 (`TrimMemory`) <!-- id: 1.4 -->
- [x] **任务二：CLI 运维端集成 (Cli)** <!-- id: 2 -->
  - [x] 在 `Program.cs` 中增加 `--check-browser` 与 `--fix-browser` 命令行参数支持 <!-- id: 2.1 -->
- [x] **任务三：UI 仪表盘 BrowserDoctor 卡片集成 (WinUI 3)** <!-- id: 3 -->
  - [x] 在 `DashboardPage.xaml` 中添加 BrowserDoctor 状态与 4 宫格卡片 <!-- id: 3.1 -->
  - [x] 在 `DashboardPage.xaml.cs` 中绑定体检与自愈动作 <!-- id: 3.2 -->
- [x] **任务四：全量编译与自测验证** <!-- id: 4 -->
  - [x] 确保全项目编译 0 警告 0 错误 <!-- id: 4.1 -->
  - [x] 运行 Core 21 项硬禁区断言测试 100% PASS <!-- id: 4.2 -->
  - [x] 编写验证报告，汇报给用户，**遵守约定：绝不擅自发包** <!-- id: 4.3 -->
