# 实施计划 - 规则多项目子选项细化与 Rust/Node 精准勾选清理

## 1. 现状感知与问题根因 (Context)
用户在实机使用 Rust target 扫描时反馈两个关键问题：
1. **无法针对具体项目选择性清理 (核心诉求)**：
   - 现有的“Rust 构建产物 (target)”和“Node 项目依赖 (node_modules)”作为整体单一选项呈现；
   - 扫描发现 8 处 Rust target（共 30.28 GiB，如 tern/target 占 20.50 GiB、zerogravity-src 占 5.79 GiB），或者 80 处 node_modules；
   - 用户无法单独保留当前正在开发的项目 target，而必须“要么全删要么全留”，容易导致误删正在开发中的编译缓存。
2. **UI 文本重叠严重 (视觉 BUG)**：
   - `rules.json` 中 `size_hint` 包含了超长中文注释（如 `30.28 GiB（8 处；最大 tern/target 20.50 GiB、zerogravity-src 5.79 GiB）`）；
   - 前端在未扫描状态下直接将这串长文本塞入右侧 `SizeDisplay`，且未做列宽弹性限制与文本截断，导致左侧标题与右侧容量发生严重视觉重叠撞车。

---

## 2. 详细改造方案 (Thought)

### 模块 A：底层数据与引擎支持子项目明细与精准执行
- **`CleanerEngine.cs` 数据结构升级**：
  - 新增 `SubPathItem` 记录类：
    - `Path`: 项目完整绝对路径；
    - `Name`: 友好项目展示名（如 `tern/target`）；
    - `SizeBytes`: 单独项目的实际字节数；
    - `FilesCount`: 单独项目的文件数量；
    - `IsSelected`: 单独项目的勾选状态（默认 true）。
  - `PlanRow` 增加 `List<SubPathItem> SubItems`。
- **扫描逻辑升级 (`ScanRule`)**：
  - 扫描遍历 `existing` 路径时，单独记录每个路径的 `SizeBytes` 与 `FilesCount`；
  - 按照体积从大到小降序排列（让占用最大的陈旧项目排在最前面，一目了然）。
- **执行逻辑升级 (`ExecutePlan`)**：
  - 如果规则行存在 `SubItems`，只对 `SubItems.Where(s => s.IsSelected)` 的物理路径执行清理；
  - 未勾选的项目物理路径绝对不碰，坚决保留！

### 模块 B：ViewModel 层 (`CleanerRuleRow.cs` 与 `CleanerSubItemRow.cs`)
- **消除文本重叠**：
  - 构造函数初始化时，右侧 `SizeDisplay` 在未扫描状态下固定为简洁的 `"未扫描"`，彻底杜绝括号长文本塞入右侧引起的重叠。
- **子项 ViewModel 与联动计算**：
  - 新增 `CleanerSubItemRow`：包含 `Name`, `Path`, `SizeBytes`, `SizeHuman`, `IsSelected`，双向绑定；
  - 父级 `CleanerRuleRow` 包含 `ObservableCollection<CleanerSubItemRow> SubItemRows`；
  - 子项勾选/取消勾选时，动态重新计算父级当前选中的 `SizeBytes`，并触发页面总可释放体积更新；
  - 增加 `HasSubItems`（当子项目数量 > 1 时为 true）与 `IsExpanded` 控制折叠展开。

### 模块 C：WinUI 3 界面交互升级 (`CleanerPage.xaml` / `.cs`)
- **列宽与文本重叠防护**：
  - 给标题与容量区域配置合理的 Grid 比例与弹性边距，增加 `TextTrimming="CharacterEllipsis"`，彻底杜绝重叠。
- **项目子清单折叠/展开区域**：
  - 在卡片内提供展开入口（例如：`展开查看 8 处项目明细 (按占用从大到小排序)`）；
  - 展开后展示嵌套项目列表：
    - 每个子项目独立 CheckBox；
    - 醒目显示项目名（如 `tern/target`）与完整物理路径；
    - 独立容量大号加粗展示（如 `20.50 GiB`）；
    - 提供【定位文件夹】按钮（点击直接调用资源管理器打开该项目所在位置，方便确认）。

---

## 3. 验证与自测标准 (Verification)
1. 21 项安全硬禁区自测 100% PASS。
2. 扫描包含多项目的规则（如 Rust target 或 node_modules），可以完整列出每个项目的独立体积。
3. 单独取消勾选某个子项目（如取消勾选 zerogravity-src），总可释放体积动态扣除对应大小。
4. 执行清理后，已取消勾选的项目文件完好无损，被勾选的项目被安全清理。
5. 界面不再发生任何文字重叠与挤压。
