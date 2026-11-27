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
- [ ] **阶段一：研发 `StartupDoctor`（启动项扫描与优化引擎）** <!-- id: 4 -->
  - [ ] 参考 `C:\Users\36230\SystemTools\扫启动项.ps1`，实现 8 维启动项与底层钩子/角标全量扫描 <!-- id: 4.1 -->
  - [ ] 构建安全白名单与风险评级（保护代理、PawnIO、核心驱动，不直接删键值，支持无损禁用与还原） <!-- id: 4.2 -->
  - [ ] 新增 WinUI 3 启动项管理页面与 CLI `--check-startup` 命令 <!-- id: 4.3 -->
- [ ] **阶段二：整合 `SystemTools` 常用系统工具** <!-- id: 5 -->
  - [ ] 将 `Explorer-角标开关` 能力并入 `ExplorerDoctor` <!-- id: 5.1 -->
  - [ ] 将 `网络诊断.ps1` / `深信服开关` 的安全诊断逻辑并入 `ProxyDoctor` <!-- id: 5.2 -->
  - [ ] 梳理 `微信文件归集` 等独立实用工具的统一调用或入口整合 <!-- id: 5.3 -->
