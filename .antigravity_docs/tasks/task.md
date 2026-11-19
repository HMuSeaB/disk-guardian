# 任务看板 (Task Board)

- [x] **任务一：任务栏与窗口图标双重保活修复** <!-- id: 1 -->
  - [x] 在 `DiskGuardian.UI.csproj` 中配置 `disk-guardian.ico` 拷贝至输出目录 <!-- id: 1.1 -->
  - [x] 在 `MainWindow.xaml.cs` 中注入 `SetCurrentProcessExplicitAppUserModelID` <!-- id: 1.2 -->
  - [x] 实现 `AppWindow.SetIcon` 与 Win32 `WM_SETICON` 双重兜底 <!-- id: 1.3 -->
- [x] **任务二：规则分类与单项快速选择逻辑实现 (ViewModel & Helper)** <!-- id: 2 -->
  - [x] 实现规则分类识别引擎（开发编译、显卡游戏、常用应用、系统临时、虚拟容器） <!-- id: 2.1 -->
  - [x] 在 `CleanerRuleRow` 中集成分类属性、分类徽章与单选触发事件 <!-- id: 2.2 -->
- [x] **任务三：CleanerPage 界面重塑与横向滑动条消除** <!-- id: 3 -->
  - [x] 彻底移除分类栏外的 `ScrollViewer`，消除遮挡与滑动条 <!-- id: 3.1 -->
  - [x] 精炼分类按钮文案（`🌟 全部`、`💻 开发`、`🎮 游戏`、`💬 软件`、`🪟 系统`、`🐳 容器`），自然平铺自适应 <!-- id: 3.2 -->
  - [x] 规则卡片右侧新增【仅选此项】(Only) 单击按钮，一键单选无需逐个取消 <!-- id: 3.3 -->
  - [x] 完善操作工具条：新增【反选】、【全选当前】、【清空选择】、【仅选L1(推荐)】 <!-- id: 3.4 -->
- [x] **任务四：全量编译构建与自检测试** <!-- id: 4 -->
  - [x] 运行 `dotnet build` 确保 0 警告 0 错误 <!-- id: 4.1 -->
  - [x] 运行 Core 21 项安全硬禁区断言自检确保 100% PASS <!-- id: 4.2 -->
- [x] **任务五：云端全自动版本发布** <!-- id: 5 -->
  - [x] 推送代码至远程 `master` 分支 <!-- id: 5.1 -->
  - [x] 打 tag 并推送 `v1.0.7` 触发 GitHub Actions 全自动构建与 Release <!-- id: 5.2 -->
