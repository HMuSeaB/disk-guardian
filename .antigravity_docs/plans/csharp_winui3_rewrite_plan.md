# 纯 C# (.NET 9) / WinUI 3 客户端重写实施计划 (Implementation Plan)

## 1. 核心目标与技术选型
彻底摆脱对本地 Python 环境的依赖，采用微软最新现代桌面技术栈打造一套独立的 Windows 11 Fluent 风格原生客户端：
- **目标框架**：.NET 9 (`net9.0-windows10.0.22621.0`)
- **UI 框架**：Windows App SDK / WinUI 3 (Unpackaged 免打包绿色模式)
- **核心库**：`CommunityToolkit.Mvvm` + `WinUIEx` (Mica 与窗口材质管理)
- **底层契约**：严格 100% 继承并固化原 Python 引擎的 **35 条硬禁区 (HARD_DENY)、21 项安全自测、两步删除语义与防卡死回收站安全清空**。

---

## 2. 解决方案分层架构

```text
src/
├── DiskGuardian.Core/          # 纯 C# 规则引擎与安全核心 (netstandard/net9 类库)
│   ├── Safety/
│   │   ├── HardDeny.cs         # 35 条绝对硬禁区、父目录递归拦截与 21 项自动化断言自测
│   │   └── RecycleManager.cs   # Win32 原生两步删除 (FO_DELETE + FOF_ALLOWUNDO) & 批次清空
│   ├── Discovery/
│   │   └── ProjectDiscoverer.cs# 基于 System.IO 的高性能目录发现器 (带 require_sibling 与 Electron 过滤)
│   ├── Engine/
│   │   ├── RuleModel.cs        # 强类型规则模型
│   │   ├── CleanerEngine.cs    # 规则编译、计划构建 (Plan) 与执行流水
│   │   └── AuditLogger.cs      # 每行立即落盘审计器 (actions.jsonl)
│   └── State/
│       └── MachineState.cs     # 25H2 蓝屏观察期与机器状态模型
│
├── DiskGuardian.Cli/           # 跨平台/原生终端工具 (支持 --check-rules, --level 等)
│
└── DiskGuardian.UI/            # WinUI 3 原生 Fluent 桌面客户端
    ├── MainWindow.xaml         # 自定义 Mica 标题栏与 NavigationView 骨架
    ├── Views/
    │   ├── DashboardPage.xaml  # 仪表盘：C/D 盘容量卡片、观察期禁区指示灯
    │   ├── CleanerPage.xaml    # 空间清理：等级切换 (L1/L2/L3)、动态规则列表、二次确认安全对话框
    │   ├── RecyclePage.xaml    # 回收站管理：大文件透视、安全分批清空
    │   └── AuditLogPage.xaml   # 审计日志：实时落盘流水查看
    └── ViewModels/             # MVVM 绑定模型
```

---

## 3. 详细实施步骤与验收标准

1. **第 1 步：搭建解决方案与 `DiskGuardian.Core` 基础工程**
   - 创建 `DiskGuardian.sln`；
   - 编写 `DiskGuardian.Core.csproj`；
   - 实现 `HardDeny.cs` 并编写 21 项安全断言自测方法（涵盖父目录拦截、`winevt` 拦截、`dedicateddumpfile.sys` 拦截等）。

2. **第 2 步：实现高性能动态发现与原生回收站管理**
   - 移植 `ProjectDiscoverer.cs`（解决 78 万文件的高性能遍历，严格排除 `\resources\app` 等 Electron 运行时）；
   - 移植 `RecycleManager.cs`（调用 Shell32 原生接口，安全两步删除，不调 `SHEmptyRecycleBinW`）。

3. **第 3 步：实现规则编译与审计引擎**
   - 移植 `CleanerEngine.cs`，直接反序列化现有的 `rules.json`；
   - 实现 `AuditLogger.cs` 记录 `actions.jsonl`。

4. **第 4 步：构建 `DiskGuardian.Cli` 命令行工具**
   - 快速提供纯 C# 命令行入口，通过命令行可直接运行 `--check-rules` 验证 21 项断言 100% 通过。

5. **第 5 步：构建 `DiskGuardian.UI` (WinUI 3)**
   - 编写 `DiskGuardian.UI.csproj`、`app.manifest` 与 XAML 页面结构；
   - 实现 Mica 材质与 Fluent Design 控件；
   - 遵循用户规则：不自动构建软件，提供标准的 Windows 终端构建与运行命令。

6. **第 6 步：全量自检与文档沉淀**
   - 产出验证报告 `.antigravity_docs/walkthroughs/walkthrough.md`。
