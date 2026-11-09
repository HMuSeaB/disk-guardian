# 实施计划 - Agent IDE 代理防线、专属图标与工程规范化 (对齐 wbmux)

## 1. 现状感知与问题根因 (Context)
1. **Agent IDE 自定义模型代理死锁是行业通用痛点**：
   - 现代 AI 编码客户端（VS Code, Cursor, Trae, WorkBuddy, ZCode, Windsurf 等）底层均为 VS Code / Electron 架构；
   - 它们决定网络外联的核心依据是 `http.proxySupport` 与 `http.proxy`；缺省时默认被判定为 `none`（直连），导致自定义模型（DeepSeek, Claude, OpenAI, TokenHarbor, TokenRouter 等）强行走直连而惨遭断网报“连接失败”；
   - 用户明确提出：应将此逻辑通用化，覆盖所有主流 Agent IDE，提供一站式体检与一键加固。
2. **应用缺少专属高质量图标 (.ico)**：
   - 启动器与 WinUI 3 应用目前未嵌入专属定制的 `.ico` 图标资源，在文件夹与桌面快捷方式中呈现默认系统图标。
3. **工程规范度需对齐工作区 `wbmux` 工业级开源标准**：
   - 根目录 README 仍为早期 Python 脚本时代陈旧内容，缺少 LICENSE、中英文双语高品质文档、ASCII Art Logo、架构拓扑与完整的生态接入说明。

---

## 2. 详细改造方案 (Thought)

### 模块 A：主流 Agent IDE 代理防线 (ProxyDoctor 通用化)
- **支持扫描的主流 IDE 列表**：
  - VS Code (`%APPDATA%\Code\User\settings.json`)
  - VS Code Insiders (`%APPDATA%\Code - Insiders\User\settings.json`)
  - Cursor (`%APPDATA%\Cursor\User\settings.json`)
  - Trae (`%APPDATA%\Trae\User\settings.json`)
  - WorkBuddy (`%USERPROFILE%\.workbuddy\settings.json`)
  - ZCode (`%APPDATA%\zcode\User\settings.json` / `%USERPROFILE%\.zcode\settings.json`)
- **数据结构扩展 (`IdeProxyStatus`)**：
  - `IdeName`, `SettingsPath`, `Installed`, `ProxySupport`, `HttpProxy`, `StatusDisplay`, `IsVulnerable`。
- **体检与加固逻辑 (`ProxyDoctor.cs`)**：
  - 启动时自动扫描已安装的 IDE；
  - 判定是否存在“已开系统代理但 IDE 缺配置导致模型断网”的弱点；
  - 一键加固：支持自动为所有受影响的 IDE 安全写入 `"http.proxySupport": "on"` 与 `"http.proxy": "http://127.0.0.1:7890"`（带自动备份）；
  - UI 界面在取证面板展示【已识别 Agent IDE 防线】，支持一键修复。

### 模块 B：专属高清应用图标设计与资源嵌入
- **制作 `disk-guardian.ico`**：
  - 纯程序化生成符合 Windows PE 规范的标准 `.ico`（包含 256x256, 128x128, 64x64, 48x48, 32x32, 16x16）；
  - 视觉语义：蓝绿色科技盾牌 + 极速存储守护盘；
- **资源编译嵌入**：
  - 嵌入 `DiskGuardian.UI.csproj` 与 `DiskGuardian.Launcher.csproj`；
  - 在 `src/launcher.rc` 中配置 `MAINICON`，通过 `windres` 编译进 Win32 C 启动器 `DiskGuardian.exe`；
  - `ShortcutManager.cs` 确保快捷方式正确显示该图标。

### 模块 C：项目工程规范化 (对齐 wbmux)
- **开源许可证**：新增标准 MIT `LICENSE`；
- **中英文标准开源文档**：
  - `README.md`：居中 ASCII Art Logo、科技感徽章、系统架构拓扑 Mermaid、三大核心守护特性（空间治理、Agent IDE 代理防线、防误杀硬禁区）、CLI 命令矩阵；
  - `README_EN.md`：完整对齐的英文版标准开源文档；
- **代码与打包脚本完善**：
  - 更新 `build_release.ps1` 包含图标打包与 Release 生成。

---

## 3. 验证与自测标准 (Verification)
1. 21 项安全硬禁区自测 100% PASS。
2. `ProxyDoctor` 准确识别本机已安装的 Agent IDE（VS Code、Trae、WorkBuddy、Cursor 等）的代理设置状态。
3. 一键加固后，对应 IDE 的 `settings.json` 正确注入 `"http.proxySupport": "on"`，自定义模型测试畅通。
4. 生成的 `DiskGuardian.exe` 与桌面快捷方式完美呈现专属应用图标。
5. 解决方案编译 0 警告、0 错误。
