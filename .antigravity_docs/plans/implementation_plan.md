# 文档与记忆重构实施计划 (Implementation Plan - 包含蓝屏事故证据溯源)

## 核心定位：蓝屏记录是规则的“根”
用户指出：“蓝屏记录也是必要的一部分，对吧”。这一洞察直指本项目的核心命脉：
**`disk-guardian` 绝非普通磁盘清理脚本，其不可动摇的硬禁区（HARD_DENY）全部诞生于 2026-11-04~11-06 本机的 0x1E/0xA/0x50/0xBE 蓝屏事故排查！**
如果失去蓝屏记录，所有的 `HARD_DENY`（禁清 `winevt`、禁清 `dmp`、保护 `dedicateddumpfile.sys`、禁碰 `WinSxS`）就会沦为无源之水。

## 详细实施步骤

### 1. 架构与目录分层
在 `docs/` 下建立四维清晰结构：
- `docs/architecture/`：系统架构设计
  - `evidence-ledger-design.md`（五层证据台账体系架构）
- `docs/incidents/`：**【核心新增】事故取证与禁区溯源（事故台账）**
  - `2026-11-04-bsod-incident-and-rules.md`：记录来酷 Bellator N176 (8940HX/5060/25H2) 蓝屏事故要点，明确“哪条蓝屏教训催生了哪条 HARD_DENY 禁区”，与 `DOUZHANZHE-Control/tools/crash/` 形成跨仓库锚定。
- `docs/research/`：逆向取证与规则分析
  - `dism-reverse-and-integration.md`（Dism++ 71 条规则黑盒取证与差异化整合方案，合并原《评估》结论）
  - `archive/2026-11-07-wheel-research.md`（原《调研》归档，文首标明“Dism++ 不可魔改”已被逆向推翻）
- `docs/data/`：机器数据
  - `dism-rules-inventory.json`
- `docs/README.md`：根目录总导航与索引，让读者一眼看清架构、事故、逆向与数据的关系。

### 2. .workbuddy/memory/MEMORY.md 规范化
- 设立四大精炼章节：
  1. 归集与定位（与 `SystemTools\索引.md` 对齐）
  2. **蓝屏事故证据与 25H2 观察期禁区（锚定 11/04 蓝屏报告与 HARD_DENY 出处）**
  3. 核心设计铁律（只读预览、两步删除、禁止 `SHEmptyRecycleBinW`、落盘审计）
  4. 运行与编码规范（WebUI 回环+令牌、`.bat` 生成器原则）
- 彻底剔除会话式流水账（如“推翻上一节的判断”）、大段重复复述。

### 3. 自检与验证 (Verification)
- 运行 `python guardian.py --check-rules` 确保 18 项自检断言 100% PASS。
- 确认 git 状态健康，无误删误伤。
- 产出 `.antigravity_docs/walkthroughs/walkthrough.md`。
