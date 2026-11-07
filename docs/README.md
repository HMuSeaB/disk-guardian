# disk-guardian 知识库与设计文档中心

本项目不是通用的“一键垃圾清理器”，而是**把真实物理机蓝屏事故与运维教训固化为不可绕过安全契约的守护系统**。
本目录存放系统的顶层架构、事故证据溯源、逆向取证与规则台账。

---

## 目录索引与导航

```text
docs/
├── README.md                           # 本文件：知识库全景导航
├── architecture/                       # 系统架构与方法论
│   └── evidence-ledger-design.md       # 五层证据台账架构 (L0 探针 ~ L4 验证)
├── incidents/                          # 事故取证与禁区溯源 (核心事实源)
│   └── 2026-11-04-bsod-incident-and-rules.md # 蓝屏排障事实与 HARD_DENY 禁区出处
├── research/                           # 逆向与调研资产
│   ├── dism-reverse-and-integration.md # Dism++ 71 条规则黑盒取证与差异化整合
│   └── archive/                        # 历史归档
│       └── 2026-11-07-wheel-research.md # 早期轮子横向调研（部分结论已被取代）
└── data/                               # 结构化机器数据
    └── dism-rules-inventory.json       # Dism++ 71 条规则 JSON 盘点数据 (33 KB)
```

---

## 文档定位与阅读指引

| 文档路径 | 核心内容 | 状态 | 适用场景 |
| :--- | :--- | :--- | :--- |
| [`docs/incidents/2026-11-04-bsod-incident-and-rules.md`](incidents/2026-11-04-bsod-incident-and-rules.md) | **蓝屏事故与禁区溯源**：详细记录 2026-11-04 来酷 N176 机器的 5 次 BugCheck、证据断链教训，以及如何一对一衍生出 `HARD_DENY`。 | **基石 / 有效** | 追溯“为什么不能清理事件日志和转储”、了解 25H2 观察期禁区时阅读。 |
| [`docs/architecture/evidence-ledger-design.md`](architecture/evidence-ledger-design.md) | **证据台账架构**：五层体系契约（探针、台账、策略、执行器、验证回写），机器状态动态输入，消除散落脚本与认知断层。 | **架构 / 规划** | 扩展系统模块、引入新探针（如 REA）、对接执行器时阅读。 |
| [`docs/research/dism-reverse-and-integration.md`](research/dism-reverse-and-integration.md) | **Dism++ 黑盒取证与整合**：448KB `Data.xml` 逆向分析、PE 导出表、71 条规则冲突与吸收候选、现代开发工具链（DXCache/pnpm）空白证明。 | **调研 / 有效** | 扩充 `rules.json`、对比外部规则库、评估系统优化开关时阅读。 |
| [`docs/data/dism-rules-inventory.json`](data/dism-rules-inventory.json) | 71 条 Dism++ 清理规则的完整结构化 JSON 清单。 | **机器数据** | 自动化规则转译与批量校验脚本的输入源。 |
| [`docs/research/archive/2026-11-07-wheel-research.md`](research/archive/2026-11-07-wheel-research.md) | 社区轮子（disk-butler 等）源码调研与 GUI 选型历史记录。 | **已归档** | 追溯技术选型背景、了解 `SHEmptyRecycleBinW` 缺陷历史。 |

---

## 关联系统边界 (严禁合并仓库)

- **`DOUZHANZHE-Control`** (`D:\4rchive\Code\DOUZHANZHE-Control\tools\crash\`):
  - 存放蓝屏分析的符号级长篇报告（15 节）与 WinDbg/cdb 工具。
  - 本项目通过契约引用其事实结论，不与其合并代码。
- **`wintuner`**:
  - 提供系统注册表与优化执行能力。
  - 与本项目各自独立，仅复用安全设计与 WebUI 窗口封装模式。
- **`SystemTools\索引.md`**:
  - 本项目作为可执行工具登记于第一节（双击即改动本机）。

