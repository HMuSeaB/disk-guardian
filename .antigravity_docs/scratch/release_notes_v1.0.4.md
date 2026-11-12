## 🚀 v1.0.4 新增特性与重大改进

### 1. 🗂️ 桌面外壳与资源管理器健康守护 (ExplorerDoctor)
* **根治 Windows 11 现代多标签页丢失与回退 Win10 经典 Ribbon 痛点**：
  * 自动深度扫描注册表 `Shell Extensions\Blocked` 是否异常拦截了 Windows 11 现代 Fluent 命令栏组件 (`CLSID {e2bf9676-5f8f-435c-97eb-11607a5bedf7}`)；
  * 深度探测 `Advanced\SeparateProcess`（在单独的进程中打开文件夹）设置冲突，杜绝导致多标签页宿主脱轨分裂；
  * 智能感知当前会话运行权限（识别管理员提权引起的系统沙箱降级回退）。
* **一键安全恢复与桌面零破坏守则**：
  * 支持一键清理阻断项并重置进程策略，通过 Win32 API 广播 `WM_SETTINGCHANGE` 设置刷新；
  * 严格遵守系统桌面安全铁律：**严禁暴力强杀 `explorer.exe`**，保护 DeskBox 及桌面微件绝不塌陷变形。
* **双端联动**：
  * **WinUI 3 仪表盘**：新增高质感 ExplorerDoctor 4 宫格体检卡片、健康状态徽章与底层注册表取证折叠面板；
  * **CLI 命令行**：新增 `disk-guardian --check-shell` 与 `disk-guardian --fix-shell` 运维指令。

---

### 2. 📱 空间规则清理页面 UI 弹性自适应重构
* **弹性三列网格布局**：
  * 彻底修复非最大化或半屏小分辨率下，顶部【开始扫描勾选项】按钮被长文本挤出可视区导致文字截断的问题；
  * 状态长提示语分配为弹性伸缩列，开启 `TextTrimming="CharacterEllipsis"`，被压缩时优雅打点省略，坚决不占右侧操作区；
  * 扫描触发按钮与进度环锁定在最右侧固定列，无论窗口如何缩放永远 100% 完整展现；
* **导航侧边栏自动收缩 (`PaneDisplayMode="Auto"`)**：
  * 在窗口缩小时，左侧导航栏自动折叠为紧凑图标栏，为主工作区多释放出近 200px 宽度。

---

### 3. 🔒 核心安全与工程质量保障
* **21 项核心安全硬禁区代码化自测**：`--check-rules` 18 条硬禁区断言 + 3 条依赖发现断言全部 100% PASS；
* **全工程全量构建**：Core / Cli / Launcher / UI 四大模块编译 0 警告、0 错误。

---
**Full Changelog**: https://github.com/HMuSeaB/disk-guardian/compare/v1.0.3...v1.0.4

