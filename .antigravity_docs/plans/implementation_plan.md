# 实施计划 - 代理细节丰富化、发布包极简收纳与回收站卡顿治理

## 1. 现状感知与问题根因 (Context)
用户在实机使用中反馈了三个核心体验痛点：
1. **代理细节表达不足**：当前仅显示两行字，未能直观展现代理类型（HTTP/Socks/PAC）、服务器端口、连通性延迟、注册表二进制标志位（Flags）、环境变量与 WorkBuddy 保护状态。
2. **发布包文件散乱难找**：解压后根目录平铺了 177 个 DLL、winmd、pdb、json 文件，用户难以一眼找到可执行程序；发布 release 缺乏自动化流程。
3. **全部移入回收站导致卡顿与塞爆**：对 Temp/DXCache 等包含数千个小碎片的规则强行 `FOF_ALLOWUNDO` 移入回收站，在 `$Recycle.Bin` 中写入数千个独立 `$I/$R` 项，导致 Windows 资源管理器与回收站假死。

## 2. 详细改造方案 (Thought)

### 模块 A：代理体检详情可视化与丰富表达 (ProxyDoctor)
- **数据层 (`ProxyDoctor.cs`)**：
  - 扩展 `ProxyDiagnosisResult`，加入：
    - 代理工作模式 (`ProxyTypeDisplay`)：手动代理 / 自动 PAC / 系统直连 / 幽灵冲突
    - 代理服务器与端口 (`Server`, `Port`)、绕过列表 (`ProxyOverride`)、PAC URL
    - 注册表底层标志位解析 (`BinaryFlagsHex`, `BinaryFlagsDescription`)
    - 本地端口连通性延迟 (`LoopbackLatencyMs`)
    - 环境变量污染详情 (`UserEnvHttp`)
    - WorkBuddy 免疫状态 (`WorkBuddyMode`)
- **UI 层 (`DashboardPage.xaml` / `.cs`)**：
  - 状态栏：大图标 + 醒目标签 + 快捷操作（【重新体检】与【一键安全修复】）。
  - 中部：**4 宫格核心配置指示面板 (Fluent Metric Tiles)**（工作模式、服务器端口、本地端口健康度、WinINET 标志位）。
  - 底部：**【技术排查与底层取证详情】Expander**，结构化展示注册表、环境变量与工具链状态。

### 模块 B：发布包极简收纳架构与一键 Release 自动化
- **启动器设计 (`DiskGuardian.Launcher`)**：
  - 根目录提供轻量无窗口启动器 `DiskGuardian.exe`，自动拉起 `app/DiskGuardian.UI.exe`。
  - 打包结构：
    ```text
    disk-guardian-winui3-win-x64/
    ├── DiskGuardian.exe     (根目录双击直接启动，极速无感知)
    ├── rules.json
    ├── 使用说明.txt
    └── app/                 (收纳全部 170+ 个 DLL、winmd 与运行时依赖)
    ```
- **CI 流水线优化 (`.github/workflows/build.yml`)**：
  - 打包时剥离 `.pdb` 调试符号，生成干净的根目录结构。
  - 增加 GitHub Release 自动发布工作流（打 tag 如 `v*` 时自动创建 GitHub Release 并附带发布资产）。

###模块 C：解决回收站塞爆卡顿与双清理模式
- **引擎支持极速物理清理 (`CleanerEngine.cs`)**：
  - `ExecutePlan` 增加 `permanent = false` 选项。
  - 针对纯临时缓存（Temp, DXCache, INetCache 等）支持极速物理删除，免去 Windows Shell 生成数千个 `$I/$R` 带来的卡死。
- **界面交互升级 (`CleanerPage.xaml` / `.cs`)**：
  - 底部提供双操作按钮：【移入回收站 (可撤销)】与【直接永久清理 (极速模式)】。
  - 智能安全拦截：当待清理项包含超过 500 个散落小文件时，弹窗友好提示并推荐【直接永久清理】。
- **回收站管理页面调优 (`RecyclePage.xaml` / `.cs`)**：
  - 优化大列表加载性能，支持安全清空回收站，快速释放当前已滞留的 4,000+ 个临时文件。

## 3. 验证与自测标准 (Verification)
- 21 项硬禁区代码化自测 100% PASS。
- 本地与 CI 编译 0 警告 0 错误。
- 启动器双击秒开，解压根目录干净清爽。
- 代理面板各项指标清晰完整。
- 极速清理临时缓存毫秒级完成，回收站不再膨胀。
