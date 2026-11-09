# 任务看板 (Task Board) - Agent IDE 代理防线、专属图标与工程规范化

- [x] 任务 1: 主流 Agent IDE 通用代理防线与一键加固 (`ProxyDoctor.cs`) <!-- id: 1 -->
  - [x] 扩展 IDE 探测模型：扫描 VS Code, Cursor, Trae, WorkBuddy, Windsurf, ZCode 等设置文件
  - [x] 诊断 `http.proxySupport` 与 `http.proxy` 状态，识别“自定义模型断网”风险
  - [x] 实现 `HardenAllIdeProxies` 支持一键安全注入代理与模式修复 (含 .bak 备份)
  - [x] UI 仪表盘与 CLI 展示已识别 IDE 的代理健康状态并提供一键加固入口
- [x] 任务 2: 专属高清应用图标设计与 PE 资源嵌入 <!-- id: 2 -->
  - [x] 生成标准多尺寸 `disk-guardian.ico` 图标（盾牌与科技磁盘，包含 16~256px 6 档全尺寸链）
  - [x] 嵌入 `DiskGuardian.UI`、`DiskGuardian.Launcher` 与 Win32 C 启动器 PE 资源
  - [x] 验证桌面快捷方式与任务栏图标专属呈现
- [x] 任务 3: 工程全面规范化 (对齐 wbmux 标准) <!-- id: 3 -->
  - [x] 引入标准 MIT `LICENSE`
  - [x] 编写高质量中英文双语 `README.md` 与 `README_EN.md` (ASCII Art + Mermaid + 矩阵)
  - [x] 更新 `build_release.ps1` 包含图标嵌入与自动化打包
  - [x] 完善 `.gitattributes` 包含 `.ico` 与 `.rc` 规则
- [x] 任务 4: 21 项安全硬禁区自测与完整编译验证 <!-- id: 4 -->
- [x] 任务 5: 验证报告生成与提交推送 <!-- id: 5 -->
