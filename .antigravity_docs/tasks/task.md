# 任务看板 (Task Board) - 规则多项目子选项细化与精准勾选

- [x] 任务 1: `CleanerEngine.cs` 支持子项目明细扫描与精准过滤清理 <!-- id: 1 -->
  - [x] 定义 `SubPathItem` 记录类并挂载至 `PlanRow`
  - [x] `ScanRule` 逐项统计每个子项目的体积与文件数，按体积降序排列
  - [x] `ExecutePlan` 支持仅清理子项中勾选的目标路径，保留未勾选项
- [x] 任务 2: ViewModel 升级与布局文字重叠修复 <!-- id: 2 -->
  - [x] 修复 `CleanerRuleRow` 初始状态将长注释放进 `SizeDisplay` 导致的文字重叠
  - [x] 创建 `CleanerSubItemRow` 支持子项目独立勾选与父级联动汇总
  - [x] 增加 `IsExpanded`, `HasSubItems`, `SubItemsSummary` 等交互属性
- [x] 任务 3: `CleanerPage.xaml` / `.cs` 前端多项目展开与勾选交互 <!-- id: 3 -->
  - [x] 修复 Grid 列宽与 TextBlock 弹性截断，杜绝布局碰撞
  - [x] 增加子项目展开/收起面板与嵌套子列表
  - [x] 支持点击子项目【定位】按钮原生打开 Windows 资源管理器查看项目
  - [x] 底部可释放空间与勾选联动实时计算
- [x] 任务 4: 21 项安全硬禁区自测与完整编译验证 <!-- id: 4 -->
- [x] 任务 5: 验证报告生成与提交推送 <!-- id: 5 -->
