# 任务看板：代理退出残留净化与纯净游戏直连模式 (Pure Direct)

## 当前任务目标
支持用户在正常使用 FlClash 的同时，在需要退出代理打游戏时，彻底消除一切后台托盘残留、系统服务死锁与注册表代理注入，提供一键切断代理无缝直连的保障。

## 进度看板
- [x] **Core 诊断与直连引擎研发**:
  - [x] 在 `ProxyDoctor.cs` 中实现主流代理进程探测（FlClash, FlClashCore, FlClashHelperService, clash-verge, mihomo 等）；
  - [x] 在 `ProxyDiagnosisResult` 中加入 `RunningClients` 识别展示；
  - [x] 实现 `SwitchToPureDirect`：强制清理系统代理、清洗二进制 Flags、广播 WinINET 变更并调用 `DnsFlushResolverCache` 刷新系统 DNS；
  - [x] 安全保障：绝不修改或删除用户的 FlClash 规则、节点和订阅，保留日常科学上网能力。
- [x] **CLI 运维命令扩展**:
  - [x] 增加 `--direct` / `--force-direct` 一键切回纯净直连命令；
  - [x] 在 `--check-proxy` 中增加对后台常驻代理进程的结构化列表与直连指引输出。
- [x] **UI 仪表盘集成**:
  - [x] 在 `DashboardPage.xaml` 顶部加入【🎮 切回纯净直连 (游戏模式)】按钮；
  - [x] 在取证折叠面板中展示后台常驻代理软件；
  - [x] 绑定带二次确认与即时生效反馈的纯净直连交互。
- [x] **工程验证与文档**:
  - [x] 全方案编译 0 警告 0 错误；
  - [x] 21 项安全自测 100% 全部通过；
  - [x] 更新 `README.md` 与 `README_EN.md`。
