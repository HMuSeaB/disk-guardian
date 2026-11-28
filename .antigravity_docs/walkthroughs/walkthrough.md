# 验证报告与排查日志 (Walkthrough)

## 一、v1.0.10 发布记录 (2026-11-23)
- **Git 标签**：`v1.0.10`
- **构建交付**：GitHub Actions 全自动构建通过，发布 `disk-guardian-winui3-win-x64.zip` 与 `disk-guardian-cli-win-x64.zip`。
- **包含特性**：`BrowserDoctor`（无损释放浏览器 GPU 显存与修剪内存）、`ExplorerDoctor` 扩展 `SnippingTool` 快捷截图死锁自愈、规则目标路径展示与一键定位、侧边栏瘦身至 `185px`。

---

## 二、2026-11-27 系统卡顿全链路排障与工程沉淀日志

### 1. CS2 + 模拟器掉帧排查与 `DOUZHANZHE-Control` 深度优化
- **实机诊断发现**：
  1. **后台 GPU 算力偷窃**：`Douzhanzhe.Shell.exe` 关到托盘后只调用了 WinForms `Hide()`，未挂起 WebView2；前端 `src/index.css` 存在无限旋转/呼吸动画叠加全屏 `backdrop-filter: blur(18~30px)`，导致 `msedgewebview2.exe` (`gpu-process`) 在后台持续狂吃 **32.2% RTX 5060 3D 算力**。
  2. **热节流与频率跌落**：CPU (`Ryzen 9 8940HX`) 顶到 `85~93°C` 高温区且电源方案锁在“平衡”，频率跌至 `2.05~2.40 GHz`；8GB 显存已用 `5.8 GB` 接近溢出。
  3. **隐藏的致命打包缺陷**：导出日志 `douzhanhe-log-1795761391891.log` 实锤全软件 WMI 报错 `System.Management currently is only supported for Windows desktop applications.`。定位为 `.csproj` 中 `System.Management` 使用了 `Version="*"`，打包拉入了 `79 KB` 的 `11.0.26.53018` 跨平台 Stub DLL，导致 `WMI ACPI 固件接口` 红灯、硬件识别失败进而触发前端隐藏「风扇控制」页面（「自定义散热」点击无反应）。
- **已落地修复（已同步推送 `origin-fork/fix/memory-v2-integration`，最新提交 `e631603`）**：
  - `Form1.cs`：新增 `HideToTray()`（`_webView.Visible = false` + `MemoryUsageTargetLevel.Low` + 1500ms 防抖 `TrySuspendAsync()`）及 `IsDisposed` 防护，实机测试后台 GPU 3D 占用直接降至 **0%**；
  - 前端 `src/utils/visibility.js`、`src/index.css`、`useControlState.js`、`FanControl.jsx`、`GameProfilesPanel.jsx`：补齐 `visibilitychange` + `dz-visibility` 动画冻结、轮询节流、唤醒秒连 WebSocket；
  - `HardwareDetector.cs`：新增注册表 `HARDWARE\DESCRIPTION\System\BIOS` 兜底识别 `Bellator N176`；
  - `Douzhanzhe.API.csproj` & `Douzhanzhe.HAL.csproj`：锁定 `System.Management` 与 `PerformanceCounter` 为官方 **`8.0.0`**（发布产物恢复为 `311,984 字节` 真 Windows 运行库）；
  - `FanCurvePanel.jsx` & `bellator-cs2-cooling.json`：支持运行中热更新（边调边看）、【导入/导出配置】JSON 及新增「游戏降温」预设。

### 2. Win 键 / 回车卡死与蓝牙消失排障 → `DeviceDoctor` 落地
- **实机诊断发现**：
  - Windows `System` 事件日志每 1~3 秒狂刷 `BTHUSB (EventID 17): 本地蓝牙无线收发器失败`，导致 PnP 设备树高频重枚举风暴，直接拖垮 `StartMenuExperienceHost` (`169.6~199.5 MiB`)、`TextInputHost` (`189.6~215.6 MiB / 103~108 线程`) 与 `SearchHost`，同时引发每秒 `5.3万+` 缺页中断。
- **工程沉淀进 `disk-guardian`（提交 `3d322a8`，未打 Tag 发包）**：
  - 新增 `src/DiskGuardian.Core/Diagnostics/DeviceDoctor.cs`：15 分钟 `BTHUSB`/`USBHUB3`/`USBXHCI` 风暴事件扫描、PnP 错误设备扫描、输入/菜单宿主膨胀检测、`ResetInputHosts()` 安全刷新（不碰 `explorer.exe`）、`DisableStormBluetooth()` 零黑框临时禁用止损；
  - 集成至 `DashboardPage.xaml` / `.xaml.cs`（4 宫格 `DeviceDoctor` 卡片）及 `DiskGuardian.Cli`（`--check-devices`、`--reset-input`）。

---

## 三、验证结果汇总

| 项目 | 验证项 | 结果 |
| :--- | :--- | :--- |
| **DOUZHANZHE-Control** | `npx vitest run` | 3 个文件 39/39 全部通过 |
| **DOUZHANZHE-Control** | `.\deploy.ps1` + C# 编译 | 0 错误，前端资源同步成功 |
| **DOUZHANZHE-Control** | `System.Management.dll` 发布体积验证 | 311,984 字节 (`8.0.23.53103`) |
| **DiskGuardian** | `dotnet build DiskGuardian.UI -c Release -r win-x64` | **0 警告、0 错误** |
| **DiskGuardian** | `--check-rules` 硬禁区自测 | **21 / 21 PASS** |
| **DiskGuardian** | `--check-devices` 实机体检 | 精准捕获 26 次 BTHUSB 风暴与膨胀宿主 |

---

## 四、2026-11-28 全机优化实战与源码内置沉淀

### 1. 实机优化排查结论
- **温控与电源调度**：早先轻载 10%~21% 下 CPU 达 `83~91°C`、`4.44 GHz`。通过开启斗战者自定义风扇曲线（大扇 `3003~3007 RPM` / 小扇 `6867~7079 RPM`）并将平衡方案 `PERFEPP` 由 `10%` 调优至 `45%`（`PERFEPP1` `50%`），CPU 降至 `70~79°C`。同时定位到 `DOUZHANZHE-Control` `CpuPowerController.cs` 将 `SET_PROC_MAX_STATE` 误设为 `CPMINCORES` (`0cc5b647-...`) 导致 `cfg-office` 强锁 100% 核心不休止。
- **启动项与远程服务**：保留 `CrossDesk Service` 及自启任务，无损禁用 `GoogleUpdaterTaskUser`、`TabbitUpdaterTaskUser` 与 8 项冗余更新/遥测计划任务；过程中定位到 `StartupDoctor` 因 `schtasks` GBK 乱码与 `"task"` 误匹配导致已禁用任务仍被列为可优化项、更新器被误保护。
- **输入宿主与重复虚拟设备**：`--reset-input` 重建 `TextInputHost` 后实测 Win11 Build 26200 冷启动基线约 `205.8 MiB / 114 线程`；`VB-Audio Virtual Cable` 主实例 `ROOT\MEDIA\0001` 正常，禁用重复报错的 `ROOT\MEDIA\0002` / `0003`（Code 10）后消除异常设备告警。

### 2. 已内置代码改动（待用户手动执行编译命令）
- [StartupDoctor.cs](file:///d:/4rchive/Code/disk-guardian/src/DiskGuardian.Core/Diagnostics/StartupDoctor.cs)：`Schedule.Service` COM 枚举任务状态、修正 `ProtectedKeywords` / `RemoteControlKeywords` / `OptimizableKeywords`。
- [PowerDoctor.cs](file:///d:/4rchive/Code/disk-guardian/src/DiskGuardian.Core/Diagnostics/PowerDoctor.cs)：新增电源调度诊断与免管理员温和降温调优。
- [DeviceDoctor.cs](file:///d:/4rchive/Code/disk-guardian/src/DiskGuardian.Core/Diagnostics/DeviceDoctor.cs)：Win11 26100+ 阈值校准 + 重复 Code 10 虚拟设备安全禁用。
- [Program.cs](file:///d:/4rchive/Code/disk-guardian/src/DiskGuardian.Cli/Program.cs) & [DashboardPage.xaml](file:///d:/4rchive/Code/disk-guardian/src/DiskGuardian.UI/Views/DashboardPage.xaml) / [DashboardPage.xaml.cs](file:///d:/4rchive/Code/disk-guardian/src/DiskGuardian.UI/Views/DashboardPage.xaml.cs)：CLI 参数与 WinUI 3 仪表盘集成完毕。
