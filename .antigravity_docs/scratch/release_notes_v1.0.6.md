## 🛠️ v1.0.6 补丁小版本 (Patch Bugfix)

本版本为快速稳定性修复补丁，重点解决在特定直连状态下注册表防空引发的报错弹窗，并完善了直连模式下各大 IDE 死代理的自动清理能力。

### 1. 🐛 修复注册表备份防空异常 (The specified registry key does not exist)
* **根因定位与修复**：
  * 原先在执行代理修复与备份时，直接调用了 `key.GetValueKind(...)`；
  * 当系统处于纯净直连、注册表键值 `ProxyServer` 或 `ProxyOverride` 为空/不存在时，.NET 底层会抛出 `The specified registry key does not exist.`，导致前端弹出错误拦截窗口；
  * 现全面重构为防御性安全写入函数 `SafeExportRegValue`，增加判空与严密异常包裹，即使注册表字段被完全清空也绝不崩溃，保障静默与健壮。

### 2. 🧹 直连与死端口模式下各大 Agent IDE 残留死代理自愈
* **死代理智能净化**：
  * 当系统处于纯净直连（或 7890 代理端口关闭）时，若各大 Agent IDE（VS Code、Cursor、WorkBuddy、Trae 等）的 `settings.json` 中仍残留 `http.proxy: "http://127.0.0.1:7890"`，会导致下方体检显示一整排红色的“🔴 残留死代理”；
  * 新增 `CleanDeadIdeProxies` 能力：在点击【一键切回纯净直连】或【一键安全修复】时，自动精准识别未在监听的死端口代理，安全将其从 IDE 配置中移除，让开发工具全面恢复纯净健康的直连模式（消除红灯提示）。

---
**Full Changelog**: https://github.com/HMuSeaB/disk-guardian/compare/v1.0.5...v1.0.6
