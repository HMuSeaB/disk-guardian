# 任务看板 (Task Board)

- [x] **任务一：任务栏与窗口图标双重保活修复** <!-- id: 1 -->
  - [x] 在 `DiskGuardian.UI.csproj` 中配置 `disk-guardian.ico` 拷贝至输出目录 <!-- id: 1.1 -->
  - [x] 在 `MainWindow.xaml.cs` 中注入 `SetCurrentProcessExplicitAppUserModelID` <!-- id: 1.2 -->
  - [x] 实现 `AppWindow.SetIcon` 与 Win32 `WM_SETICON` 双重兜底 <!-- id: 1.3 -->
- [x] **任务二：规则分类与单项快速选择逻辑实现 (ViewModel & Helper)** <!-- id: 2 -->
  - [x] 实现规则分类识别引擎（开发构建、显卡游戏、常用应用、系统临时、虚拟容器） <!-- id: 2.1 -->
  - [x] 在 `CleanerRuleRow` 中集成分类属性、分类徽章与单选触发事件 <!-- id: 2.2 -->
- [x] **任务三：CleanerPage 界面重塑 (XAML & Code-behind)** <!-- id: 3 -->
  - [x] 顶部添加分类标签栏 (分类选择 Chips/Tabs) 与实时搜索框 <!-- id: 3.1 -->
  - [x] 完善操作工具条：新增【反选】、【全选当前分类】、【清空选择】、【全选】 <!-- id: 3.2 -->
  - [x] 规则卡片右侧新增【仅选此项】(Only) 单击按钮，一键单选无需逐个取消 <!-- id: 3.3 -->
  - [x] 优化卡片布局与视觉层级（Fluent 药丸标签、平滑收起/展开） <!-- id: 3.4 -->
- [x] **任务四：全量编译构建与自检测试** <!-- id: 4 -->
  - [x] 运行 `dotnet build` 确保 0 警告 0 错误 <!-- id: 4.1 -->
  - [x] 运行 Core 21 项安全硬禁区断言自检确保 100% PASS <!-- id: 4.2 -->
- [x] **任务五：生成验证报告与更新文档** <!-- id: 5 -->
  - [x] 编写 `.antigravity_docs/walkthroughs/walkthrough.md` <!-- id: 5.1 -->
