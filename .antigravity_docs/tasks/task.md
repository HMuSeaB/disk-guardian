# 任务看板 (Task Board)

- [x] **任务一：清理当前后台僵死的 SnippingTool 进程** <!-- id: 1 -->
  - [x] 成功安全清除 9 个僵死 `SnippingTool.exe` 进程，残留归 0，恢复截图响应 <!-- id: 1.1 -->
- [x] **任务二：ExplorerDoctor 扩展快捷截图通道健康诊断与自愈 (Core)** <!-- id: 2 -->
  - [x] 扩展 `ExplorerDoctor.cs`，探测 `SnippingTool` / `ScreenClippingHost` 进程堆积 <!-- id: 2.1 -->
  - [x] 实现 `ResetSnippingTool()` 一键清除死锁进程并释放屏幕捕获与剪贴板锁 <!-- id: 2.2 -->
- [x] **任务三：UI 仪表盘 ExplorerDoctor 卡片集成** <!-- id: 3 -->
  - [x] 在 `ExplorerDoctor` 卡片右侧增加【⚡ 一键重置截图】按钮 <!-- id: 3.1 -->
  - [x] 将 4 宫格 Tile 4 升级为【快捷截图通道 (Win+Shift+S)】实时指标看板 <!-- id: 3.2 -->
  - [x] 在下方展开取证区输出详细的截图工具进程与通道日志 <!-- id: 3.3 -->
- [x] **任务四：CLI 运维端命令扩展** <!-- id: 4 -->
  - [x] `--check-shell` 增加快捷截图工具健康状态展示 <!-- id: 4.1 -->
  - [x] 新增 `--reset-snipping` 命令支持终端一键自愈 <!-- id: 4.2 -->
- [x] **任务五：全量编译与自测验证** <!-- id: 5 -->
  - [x] 全项目编译 0 警告 0 错误 <!-- id: 5.1 -->
  - [x] 核心安全硬禁区 21 项断言 100% PASS <!-- id: 5.2 -->
  - [x] 遵守约定：**代码已提交并推送到 master 分支，绝不擅自发包，静待用户指令！** <!-- id: 5.3 -->
