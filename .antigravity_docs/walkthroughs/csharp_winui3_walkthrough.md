# 纯 C# / WinUI 3 原生客户端重构落地报告 (Walkthrough)

## 1. 交付架构总览

已在 `src/` 下完整实现了脱离 Python 运行时的纯 C# (.NET 9 / WinUI 3) 现代架构，并建立统一解决方案 `DiskGuardian.sln`：

```text
src/
├── DiskGuardian.sln            # 统一解决方案
├── DiskGuardian.Core/          # 纯 C# 规则引擎与安全核心类库 (netstandard/net9)
│   ├── Safety/
│   │   ├── HardDeny.cs         # 35 条代码化硬禁区、父目录递归拦截与 18+3 项自动化断言自测
│   │   └── RecycleManager.cs   # Win32 原生两步删除 (FOF_ALLOWUNDO) & 批次清空 (不碰挂死 API)
│   ├── Discovery/
│   │   └── ProjectDiscoverer.cs# 基于 System.IO 的高性能构建产物发现器 (严格排除 resources/app)
│   └── Engine/
│       ├── RuleModel.cs        # rules.json 强类型模型
│       ├── CleanerEngine.cs    # 规则编译、计划构建 (Plan) 与执行流水
│       └── AuditLogger.cs      # 每行立即落盘审计器 (actions.jsonl)
│
├── DiskGuardian.Cli/           # 纯 C# 原生命令行工具
│   └── Program.cs              # 支持 --check-rules, --level, --apply, --purge
│
└── DiskGuardian.UI/            # WinUI 3 原生 Fluent 桌面客户端
    ├── MainWindow.xaml         # Windows 11 Mica 材质与 NavigationView 骨架
    └── Views/
        ├── DashboardPage.xaml  # 仪表盘：C/D 盘容量卡片、观察期禁区指示灯
        ├── CleanerPage.xaml    # 空间清理：等级切换 (L1/L2/L3)、动态规则列表、二次确认对话框
        ├── RecyclePage.xaml    # 回收站管理：大文件透视、安全分批清空
        └── AuditPage.xaml      # 审计日志：实时落盘流水查看
```

---

## 2. 自动化自检验收结果

在命令行运行 C# 自检：
```pwsh
dotnet run --project src/DiskGuardian.Cli/DiskGuardian.Cli.csproj -- --check-rules
```

**验证结果**：
- `HARD_DENY self-test` 18 项断言全部 PASS (成功拦截 15 项危险路径，放行 3 项合法缓存)；
- `ProjectDiscoverer self-test` 3 项断言全部 PASS (项目依赖被发现、Electron 运行时被排除、无清单目录被跳过)；
- **全量 21 项安全断言 100% 通过**，退出码 `rc=0`。

在命令行运行 Level 1 快速预览：
```pwsh
dotnet run --project src/DiskGuardian.Cli/DiskGuardian.Cli.csproj -- --level 1
```
- 输出结构与原 Python 引擎完全对齐，毫秒级返回，可清理 575.9 MiB。

---

## 3. 编译与启动命令行

根据全局规则（不自动构建图形软件，提供用户终端命令行）：

### 方式 1：双击批处理启动（最简单）
- 双击工作区内：[`launchers\6-启动WinUI3原生客户端.bat`](file:///d:/4rchive/Code/disk-guardian/launchers/6-启动WinUI3原生客户端.bat)
- 或双击系统工具箱：`C:\Users\36230\SystemTools\disk-guardian\6-启动WinUI3原生客户端.bat`

### 方式 2：Windows 终端编译与运行
```pwsh
# 1. 编译并运行 WinUI 3 原生 Fluent 客户端
dotnet run --project src/DiskGuardian.UI/DiskGuardian.UI.csproj

# 2. 编译并运行原生 CLI 快速体检
dotnet run --project src/DiskGuardian.Cli/DiskGuardian.Cli.csproj -- --check-rules

# 3. 也可以直接用 VS / Rider 打开整体工程
# D:\4rchive\Code\disk-guardian\src\DiskGuardian.sln
```
