# 任务看板 (Task Board)

## 已归档里程碑
- [x] **v1.0.10 正式发版与交付**（BrowserDoctor + SnippingTool 死锁自愈 + 规则路径定位） <!-- id: 1 -->
- [x] **CS2 + 模拟器掉帧排查与 `DOUZHANZHE-Control` 深度改造** <!-- id: 2 -->
  - [x] 定位 `Douzhanzhe.Shell` 后台吃掉 32.2% GPU 算力根因并落地 `TrySuspendAsync` + 前端节流（已验证 0% GPU） <!-- id: 2.1 -->
  - [x] 修复 `System.Management` 通配符导致 WMI ACPI 失效与「自定义散热」无法点击的 Bug <!-- id: 2.2 -->
  - [x] 落地 `FanCurvePanel` 导入/导出与热更新调参 + `bellator-cs2-cooling.json` 配置 <!-- id: 2.3 -->
- [x] **`BTHUSB` 重连风暴排查与 `DeviceDoctor` 沉淀进 `disk-guardian`** <!-- id: 3 -->
  - [x] 完成 `DeviceDoctor.cs`（15 分钟事件风暴扫描 + PnP 错误码 + 输入宿主刷新 + 蓝牙临时止损） <!-- id: 3.1 -->
  - [x] 完成 WinUI 3 仪表盘卡片与 CLI (`--check-devices` / `--reset-input`) 集成 <!-- id: 3.2 -->
  - [x] 全量编译 0 警告 0 错误，21 项安全自测 PASS，已推送 `master`（未打 Tag 发包） <!-- id: 3.3 -->

---

## 下一阶段计划：启动项优化与 `SystemTools` 工具整合
- [x] **阶段一：研发 `StartupDoctor`（启动项扫描与优化引擎）** <!-- id: 4 -->
  - [x] 参考 `C:\Users\36230\SystemTools\扫启动项.ps1`，实现 8 维启动项与底层钩子/角标全量扫描 <!-- id: 4.1 -->
  - [x] 构建安全白名单与风险评级（保护代理、PawnIO、核心驱动，不直接删键值，支持无损禁用与还原） <!-- id: 4.2 -->
  - [x] 新增 WinUI 3 启动项管理页面与 CLI `--check-startup` 命令 <!-- id: 4.3 -->
- [x] **阶段二：整合 `SystemTools` 常用系统工具** <!-- id: 5 -->
  - [x] 将 `Explorer-角标开关` 能力并入 `ExplorerDoctor` <!-- id: 5.1 -->
  - [x] 将 `网络诊断.ps1` / `深信服开关` 的安全诊断逻辑并入 `ProxyDoctor` <!-- id: 5.2 -->
  - [x] 将 `微信文件归集` 等独立实用工具的统一调用或入口整合 <!-- id: 5.3 -->
  - [x] 为 `StartupPage` 增加【⚡ 一键优化冗余项】批量无损禁用，为 `DashboardPage` 增加【🚀 一键全盘体检】 <!-- id: 5.4 -->

---

## 阶段三：2026-11-28 实机排障沉淀内置化（StartupDoctor 修复 + PowerDoctor + DeviceDoctor 升级）
- [x] **`StartupDoctor` 精准化改造** <!-- id: 6 -->
  - [x] 计划任务枚举升级为 `Schedule.Service` COM `Task.Enabled`（彻底消除中文 Windows GBK 乱码漏检与 CSV 引号错位，保留 `schtasks` 兜底） <!-- id: 6.1 -->
  - [x] 修复 `ProtectedKeywords` 裸词 `"task"` 误保护更新器，并将 `CrossDesk` 等远程服务移入 `Caution` 保护不进一键优化 <!-- id: 6.2 -->
  - [x] CLI 补齐 `--optimize-startup` <!-- id: 6.3 -->
- [x] **新增 `PowerDoctor` 电源调度与温控偏好医生** <!-- id: 7 -->
  - [x] 实现 `PowerDoctor.cs`（诊断 `PERFEPP` / `PERFEPP1` / `CPMINCORES` / `PERFBOOSTMODE`，提供免管理员温和调优 `45%/50%` 与恢复） <!-- id: 7.1 -->
  - [x] 接入 `DashboardPage` 4 宫格卡片（纳入全盘体检）与 CLI (`--check-power` / `--optimize-power` / `--restore-power`) <!-- id: 7.2 -->
- [x] **`DeviceDoctor` 校准与冗余虚拟设备治理** <!-- id: 8 -->
  - [x] 针对 Win11 Build >= 26100 校准输入/菜单宿主基线阈值，消除重置后假阳性告警 <!-- id: 8.1 -->
  - [x] 新增 Code 10 重复虚拟设备识别与 `DisableSafeErrorDevices`（接入仪表盘按钮与 CLI `--disable-error-devices`） <!-- id: 8.2 -->
- [ ] **待用户手动在 Windows 终端构建验证**（遵循全局规则“不自动构建软件，只提供命令行”） <!-- id: 9 -->
