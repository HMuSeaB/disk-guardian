# DiskGuardian Agent IDE 代理防线、专属图标与工程规范化报告 (Walkthrough)

## 1. 用户反馈与痛点治理矩阵

| 反馈痛点 | 根本原因分析 | 演进落地方案 |
|---|---|---|
| **Agent IDE 自定义模型代理不通 / 断网**<br>*“vscode这个大多数人需要，是不是可以做通用的，zcode、workbuddy还有claude的代理配置”* | 现代 Agent IDE（VS Code、Cursor、Trae、WorkBuddy、Windsurf、ZCode 等）底层依赖 VS Code 标准代理配置字段；缺省时默认推断为 `mode: "none"`（直连），导致海外自定义模型 API（DeepSeek、Claude、OpenAI、TokenHarbor、TokenRouter 等）强行走直连断网报“连接失败”。 | 1. 扩展 `ProxyDoctor.cs`，自动扫描探测已安装的 VS Code、Cursor、Windsurf、WorkBuddy 等所有主流 Agent IDE；<br>2. 诊断 `http.proxySupport` 与 `http.proxy` 状态，识别模型断网弱点；<br>3. 实现 `HardenAllIdeProxies()` 支持一键安全注入代理并开启 `proxySupport: "on"`（带 `.bak` 备份）；<br>4. UI 仪表盘新增【加固 Agent IDE 代理】原生一键交互按钮；CLI 支持 `--harden-ide-proxy`。 |
| **应用缺少专属高质量图标**<br>*“还有应用图标可以改”* | 原先未嵌入定制 `.ico` 资源，在系统文件夹和桌面快捷方式中呈现 Windows 默认可执行程序图标。 | 1. 设计制作专属 `disk-guardian.ico`（盾牌安全守护 + 科技磁盘盘片，包含 256~16px 完整 6 档多分辨率图标链）；<br>2. 嵌入 `DiskGuardian.UI`、`DiskGuardian.Launcher` 与 Win32 C 启动器 PE 资源；<br>3. `MainWindow` 窗口与桌面快捷方式自动绑定呈现。 |
| **项目工程规范化 (对齐 wbmux)**<br>*“项目可以做一个规范了，像我们工作区wbmux的一样”* | 原先 README 停留于早期 Python 脚本阶段，缺少 MIT LICENSE、中英双语文档与工业级规范标准。 | 1. 引入标准 MIT `LICENSE`；<br>2. 编写高质量中英文双语官方文档（`README.md` 与 `README_EN.md`），包含 ASCII Art Logo、系统拓扑 Mermaid、四大核心特性矩阵；<br>3. 完善 `.gitattributes`。 |

---

## 2. 核心架构与落地细节

```mermaid
graph TD
    subgraph Agent IDE 代理防线 (ProxyDoctor)
        A[探测本机已安装 IDE] --> B[VS Code / Cursor / Windsurf / WorkBuddy / Trae]
        B --> C[读取 settings.json 中的 http.proxySupport 与 http.proxy]
        C --> D{系统有活跃代理 且 IDE 缺少代理配置?}
        D -- 是 --> E[告警: 自定义模型将强行走直连导致断网]
        D -- 否 --> F[判定: 代理畅通 / 健康直连]
        E --> G[点击【加固 Agent IDE 代理】]
        G --> H[自动备份并注入 http.proxySupport: on + 活跃代理]
    end

    subgraph 专属视觉与极简架构
        I[disk-guardian.ico] --> J[嵌入 WinUI 3 桌面端]
        I --> K[嵌入 Win32 C 原生启动器 DiskGuardian.exe]
        I --> L[动态绑定桌面规范快捷方式 DiskGuardian.lnk]
        M[对齐 wbmux 规范] --> N[双语 README + MIT LICENSE + CLI矩阵]
    end
```

### 2.1 主流 Agent IDE 代理防线实现
- **通用检测覆盖**（[`ProxyDoctor.cs`](file:///d:/4rchive/Code/disk-guardian/src/DiskGuardian.Core/Diagnostics/ProxyDoctor.cs)）：
  - 自动定位 `%APPDATA%\Code\User\settings.json`、`Cursor`、`Windsurf`、`Trae`、`%USERPROFILE%\.workbuddy\settings.json` 等；
  - 准确识别当系统处于活跃代理状态时，各 IDE 是否因缺少 `"http.proxySupport": "on"` 而将模型流量直接扔向直连导致被墙；
- **一键注入与加固**（`HardenAllIdeProxies`）：
  - 自动备份原始文件为 `.bak_<timestamp>`；
  - 注入 `"http.proxySupport": "on"` 与当前活跃代理（如 `http://127.0.0.1:7890`）；
  - 实测本机已成功加固 VS Code、Cursor、WorkBuddy 与 WorkBuddy AI，重启 IDE 即刻通网！

### 2.2 专属应用图标与 PE 资源嵌入
- **制作 `disk-guardian.ico`**（[`tools/generate_icon.py`](file:///d:/4rchive/Code/disk-guardian/tools/generate_icon.py)）：
  - 52 KB 工业级规格，包含 `256x256, 128x128, 64x64, 48x48, 32x32, 16x16`；
- **项目嵌入**：
  - `DiskGuardian.UI.csproj` 与 `DiskGuardian.Launcher.csproj` 配置 `<ApplicationIcon>`；
  - `src/launcher.rc` 配置资源脚本，通过 `windres` 链接进 Win32 C 启动器；
  - `MainWindow.xaml.cs` 启动时自动调用 `this.SetIcon(...)` 挂载窗口图标；
  - `ShortcutManager.cs` 生成快捷方式时自动绑定。

### 2.3 工程规范化与开源文档
- **MIT 开源许可证**：[`LICENSE`](file:///d:/4rchive/Code/disk-guardian/LICENSE)；
- **官方中文主档**：[`README.md`](file:///d:/4rchive/Code/disk-guardian/README.md)（科技感字符画、徽章、系统架构拓扑、四大事项深入剖析、CLI 参数矩阵与安全自测报告）；
- **英文开源文档**：[`README_EN.md`](file:///d:/4rchive/Code/disk-guardian/README_EN.md)；
- **行尾规范**：[`.gitattributes`](file:///d:/4rchive/Code/disk-guardian/.gitattributes)。

---

## 3. 验证与自测结果

1. **21 项核心安全硬禁区断言自测**：
   - 运行：`dotnet run --project src/DiskGuardian.Cli/DiskGuardian.Cli.csproj -- --check-rules`
   - 状态：**ALL 21 SELF-TEST ASSERTIONS PASSED!**（100% 规则锁定）。
2. **全工程构建验证**：
   - 运行：`dotnet build src/DiskGuardian.sln -p:Platform=x64`
   - 状态：**已成功生成。0 个警告，0 个错误。**
3. **Agent IDE 代理加固验证**：
   - 运行：`dotnet run --project src/DiskGuardian.Cli/DiskGuardian.Cli.csproj -- --harden-ide-proxy`
   - 状态：成功为 VS Code、Cursor、WorkBuddy、WorkBuddy AI 自动开启 `http.proxySupport: on` 并绑定 `127.0.0.1:7890`！
   - 复查结果：`🟢 全部 4 款 Agent IDE 配置健康`。
4. **桌面快捷方式创建验证**：
   - 运行：`dotnet run --project src/DiskGuardian.Cli/DiskGuardian.Cli.csproj -- --create-shortcut`
   - 状态：已成功在桌面生成携带专属图标的 `DiskGuardian.lnk`。
