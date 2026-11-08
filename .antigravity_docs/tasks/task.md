# 任务看板 (Task Board) - 体验与发布三大痛点治理

- [x] 任务 1: 代理体检详情可视化与丰富表达 <!-- id: 1 -->
  - [x] 扩展 `ProxyDoctor.cs`，丰富代理类型、服务器端口、延迟、标志位解析与配置细节
  - [x] 重构 `DashboardPage.xaml`，增加 4 宫格指标卡片与【技术排查与底层取证详情】Expander
  - [x] 更新 CLI `--check-proxy` 格式化排查输出
- [x] 任务 2: 解决“移入回收站太卡 / 回收站被塞入几千个文件” <!-- id: 2 -->
  - [x] `CleanerEngine.cs` 增加永久直清能力 (`permanent: true`)
  - [x] `CleanerPage.xaml` 增加【直接永久清理 (极速模式)】操作按钮
  - [x] 大量小碎片文件（>500 项）弹窗智能推荐分流，避免 Windows 回收站卡死
  - [x] 调优 `RecyclePage.xaml` 帮助用户一键快速清空当前被塞满的回收站
- [x] 任务 3: 发布包极简收纳与 Release 自动化发布 <!-- id: 3 -->
  - [x] 增加轻量启动器 `DiskGuardian.Launcher`，打包生成根目录 `DiskGuardian.exe`
  - [x] 优化 `.github/workflows/build.yml`：收纳 170+ 个 DLL 至 `app/` 目录，去除 `.pdb`
  - [x] 支持打 Tag 自动发布 GitHub Release 与产物上传
- [x] 任务 4: 本地编译、21 项断言自测与功能验证 <!-- id: 4 -->
- [x] 任务 5: 推送至 master，触发并验证 GitHub Actions 流水线 <!-- id: 5 -->
