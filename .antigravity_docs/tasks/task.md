# 任务看板 (Task Board) - ProxyDoctor 幽灵代理诊断修复

- [x] 任务 1: 核心诊断与修复库 (`DiskGuardian.Core/Diagnostics/ProxyDoctor.cs`) <!-- id: 1 -->
  - [x] 注册表 DWORD 与二进制解析
  - [x] 本地回环端口可用性探测
  - [x] 环境变量与 WorkBuddy 弱点检测
  - [x] 安全备份、清洗与 WinINet API 广播刷新
- [x] 任务 2: CLI 工具接入 `--check-proxy` 与 `--fix-proxy` <!-- id: 2 -->
  - [x] 参数解析与格式化控制台输出
  - [x] 退出码逻辑 (0=健康, 1=异常)
- [x] 任务 3: WinUI 3 仪表盘 Fluent 卡片与交互逻辑 (`DashboardPage`) <!-- id: 3 -->
  - [x] UI 卡片设计（健康/异常指示灯、信息摘要）
  - [x] 【一键安全修复】异步处理与实时刷新
- [x] 任务 4: 编译、断言测试与实机验证 <!-- id: 4 -->
- [x] 任务 5: 推送至 master 并验证 GitHub Actions CI 产物 <!-- id: 5 -->
