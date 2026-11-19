## 🚀 v1.0.5 新增特性与重大改进

### 1. 🎮 纯净直连模式 (Pure Direct / Game Mode)
* **专为打游戏、局域网直连或退出代理设计**：
  * 根治退出代理软件后游戏被代理注入、高延迟或被防作弊（ACE、Vanguard、Easy Anti-Cheat、BattlEye）拦截断网的绝症；
  * **一键安全净化**：
    1. 优雅退出后台托盘驻留的代理客户端（FlClash、Clash Verge 等），**100% 完整保留用户的所有订阅、节点与配置文件，绝不损坏日常使用**；
    2. 强制将 Windows 注册表 `ProxyEnable` 重置为 `0`，清空 `ProxyServer`；
    3. 清洗 `DefaultConnectionSettings` 与 `SavedLegacySettings` 底层二进制标志为 `0x01`（直连）；
    4. 清除残留的用户环境变量（`HTTP_PROXY` / `HTTPS_PROXY` / `ALL_PROXY`）；
    5. 通过 Win32 WinINET 原生 API 广播配置更新（全局即刻生效，**坚决不杀 explorer.exe**，保护桌面微件安全）；
    6. 调用 Win32 原生 DNS API (`DnsFlushResolverCache`) 毫秒级刷新系统 DNS 解析缓存，清除代理 Fake-IP 域名污染。

### 2. 🩺 后台常驻代理软件与系统服务深度感知
* **终结工具箱“闭眼正常”的逻辑盲区**：
  * 原先工具箱仅排查端口闭塞（死端口），当代理软件正在监听时默认展示正常；
  * 现全面接入后台进程与服务扫描，精准识别 `FlClash.exe`（托盘/前台）、`FlClashCore.exe`（内核）、`FlClashHelperService`（系统服务）、`clash-verge`、`mihomo` 等驻留状态；
  * 在状态栏与排障取证面板中直观高亮展示常驻 PID 与运行模式，并在检测到后台驻留时主动给出游戏直连指引。

### 3. 🖥️ 双端交互矩阵全面联动
* **WinUI 3 桌面端 (DashboardPage)**：
  * 仪表盘新增显眼的 **【🎮 切回纯净直连 (游戏模式)】** 按钮；
  * 排查折叠面板增加“后台常驻代理软件与系统服务”实时清单；
  * 纯净直连支持二次确认防误触，执行后即刻刷新生效。
* **CLI 极速运维端**：
  * 新增 `disk-guardian --direct`（或 `disk-guardian --force-direct`）指令，支持终端一秒完成直连净化；
  * `disk-guardian --check-proxy` 同步输出后台常驻客户端与游戏直连建议。

### 4. 🔒 核心工程质量保障
* **21 项核心安全硬禁区代码化断言自测**：`--check-rules` 18 条硬禁区断言 + 3 条依赖发现断言全部 100% PASS；
* **全工程全量构建**：Core / Cli / Launcher / UI 四大模块编译 0 警告、0 错误。

---
**Full Changelog**: https://github.com/HMuSeaB/disk-guardian/compare/v1.0.4...v1.0.5
