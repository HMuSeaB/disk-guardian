# 任务清单：极简现代设计语言重构 (App Icon & README)

## 任务目标
深度对齐用户桌面环境（如 `wbmux`、`AnyCursor`、`Cursor Launcher`）与高端 GitHub 知名开源项目（Raycast、Linear、Zed、Tauri），彻底废弃生硬拟物和粗糙 ASCII，全面换装 2D 扁平极简矢量 Squircle 图标体系与顶级项目门面。

## 任务进度看板

- [x] **设计解构与视觉调研**:
  - [x] 分析用户真实收纳文件夹中的设计语言：纯粹 Squircle 底座、单一高反差对比色、50%~55% 舒适呼吸留白、高识别度极简几何符号；
  - [x] 确立 DiskGuardian 标志性超级符号：现代几何护盾（Guardian）+ 嵌套同心同轴数据磁道与电光青蓝扫描弧（Disk Storage Platter）。
- [x] **生产级矢量资产与多尺寸 ICO 生成**:
  - [x] 生成 1024x1024 高精源图 `assets/icon-source.jpg`；
  - [x] 精准裁切 768x768 连续曲率 Squircle 并在超高分辨率下通过 SSAA 与高斯平滑羽化生成透明 Alpha 通道；
  - [x] 导出 `assets/icon.png` (512x512) 与 `assets/icon-256.png`；
  - [x] 导出包含 256x256 ~ 16x16 完整 6 档尺寸链的 Windows PE 图标 `disk-guardian.ico`；
  - [x] 重构自动化构建脚本 `tools/generate_icon.py`。
- [x] **可执行程序与桌面快捷方式刷新**:
  - [x] 使用 `windres` 重新编译资源并链接生成 `DiskGuardian.exe`；
  - [x] 自动更新桌面快捷方式 `DiskGuardian.lnk`，实测完美呈现纯黑白翠青极简 Squircle 图标；
  - [x] 运行 21 项安全硬禁区代码化自测（100% 全部 PASS）。
- [x] **中英文官方门面重构 (README.md / README_EN.md)**:
  - [x] 彻底移除古旧生硬的巨幅 ASCII Art，对齐顶尖开源项目排版；
  - [x] 居中内嵌高质量 `assets/icon.png` 品牌徽标，搭配沉浸感圆角与环境阴影；
  - [x] 提炼精纯的一句话价值主张（One-liner）与模块化拓扑结构。
