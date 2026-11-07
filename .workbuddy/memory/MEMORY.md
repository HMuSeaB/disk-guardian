# disk-guardian 长期记忆

## 1. 归集与核心定位

- **代码与入口**：
  - 源码仓库：`D:\4rchive\Code\disk-guardian\`（独立 git 仓库）。
  - 双击入口：`C:\Users\36230\SystemTools\disk-guardian\`，已登记在 `C:\Users\36230\SystemTools\索引.md` **第一节**（改动本机文件，归「一」）。
- **工具定位**：**Dism++ 的补集，而非替代品**。
  - Dism++ 管 WinSxS / 驱动 / 系统基线；
  - 本工具专管现代 Shader 缓存（DXCache/GLCache）、包管理器（npm/pnpm/cargo/NuGet）与开发构建产物（Dism++ 71 条规则对此全部 0 命中）。
- **知识库体系**：设计与证据索引见 [`docs/README.md`](../../docs/README.md)。

## 2. 蓝屏事故证据与 25H2 观察期禁区 (Provenance)

- **事故根源**：详见 [`docs/incidents/2026-11-04-bsod-incident-and-rules.md`](../../docs/incidents/2026-11-04-bsod-incident-and-rules.md)。
  - 2026-11-04 来酷 Bellator N176 (8940HX/5060/25H2) 发生多次内核空闲崩溃（0x1E/0xA/0x50/0xBE），排障时因前序清理工具导致日志与早前 dmp 断链，教训极其深刻。
  - 系统当前处于 **25H2 蓝屏观察期**，已配置专用转储 `dedicateddumpfile.sys` 与驱动验证器。
- **不可动摇的硬禁区 (HARD_DENY)**：
  - **禁区位置铁律**：`HARD_DENY` **必须硬编码在 `guardian.py` 常量中，绝不能搬进 `rules.json`**（规则表是数据，禁区是不可篡改的代码约束）。同时必须严格拦截“目标作为受保护路径的父目录”。改动后必须执行 `python guardian.py --check-rules`，18 条断言全 PASS。
  - **禁止清理对象**：
    1. `winevt\Logs`（Windows 事件日志，销毁蓝屏证据，绝对禁止）；
    2. `MEMORY.DMP` / `Minidump` / `CrashDumps`（观察期保留现场）；
    3. `dedicateddumpfile.sys`（专用转储文件）；
    4. `assembly\NativeImages_*`（实测清空后 NGEN 后台死循环重建反噬 2.5 GiB）；
    5. `Installer` 目录（破坏 MSI 安装卸载底层）；
    6. 观察期内全面冻结 `WinSxS` 组件清理与系统还原点。
  - **规则转译红线**：Dism++ 的 Level 等级（L0~L3）绝不能直接映射到本工具 level，吸收外来规则必须逐条人工核定边界与适用范围。

## 3. 核心设计铁律 (Invariants)

1. **默认只读预览**：必须显式 `--apply` 并输入交互令牌（`DELETE` / `DELETE2` / `DELETE3`）才执行。
2. **两步删除语义**：`--apply` 仅移入回收站（可撤销）；`--purge` 才真正永久物理删除（令牌 `PURGE`），两步严格解耦。
3. **严禁调用 `SHEmptyRecycleBinW`**：实测遇到坏死文件（如 `$R02DUPK` / `0x80004005`）会触发系统模态框无限卡死，`SHERB_NOCONFIRMATION` 也无法阻断。清空回收站必须通过 `recycle.empty_safely()` 按 `$I`/`$R` 配对安全逐批清理。
4. **审计绝对落盘**：日志每行必须执行 `flush + fsync`，动作明细独立写 `out/actions.jsonl`，防止执行卡死时日志断流。

## 4. 运行与编码规范

- **图形界面 (WebUI)**：
  - 架构：`ui/server.py`（标准库 `http.server`）+ `ui/assets/index.html`（离线单文件）。
  - 安全约束：严格监听 `127.0.0.1`；一次性令牌鉴权（首页与 API 均校验）；Edge/Chrome `--app=` 无边框模式；不自动退出（由页面主动触发退出）。
  - 单一事实源：CLI 与 GUI **共用 `guardian.execute_rules`**，前端仅传递规则 id，后端以刚重建的 plan 复核等级与禁区，严禁复制第二份清理逻辑。
- **脚本与启动器 (.bat)**：
  - `.bat` 一律由 `tools\make_launchers.py` 自动化生成，**严禁手工编辑**。
  - 编码规则：GB18030 + CRLF + 无 BOM + `chcp 936`；文件名不含半角括号；不用 `if (...)` 括号块（防解析闪退）；显式声明 `PYTHONUTF8=1`。
  - `.gitattributes` 中 `*.bat text eol=crlf` 必须置于 `* text=auto eol=lf` 之后。
