#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""disk-guardian: rule-driven, preview-first disk cleanup for this machine.

Design rules, all learned the hard way on 2026-11-07:

  * The default invocation is a READ-ONLY preview. Nothing is deleted unless
    --apply is passed AND the operator confirms by typing a token.
  * HARD_DENY lives in this file, not in rules.json. A mistyped or maliciously
    edited rule table cannot unlock a protected path.
  * We never call SHEmptyRecycleBinW. It raised a blocking modal dialog on a
    broken item ($R02DUPK / 0x80004005) even with SHERB_NOCONFIRMATION.
  * Every action is appended to out/*.log and flushed immediately, so a hang
    never loses the record of what already happened.
  * Dism++ has no rules for shader caches or package-manager caches. This tool
    covers that gap; it is NOT a Dism++ replacement and never touches WinSxS,
    Installer, restore points, event logs or crash dumps.

Usage examples:
    python guardian.py                        # preview level 1, read-only
    python guardian.py --level 2 --json-report
    python guardian.py --apply                # level 1 -> Recycle Bin
    python guardian.py --apply --level 2      # extra confirmation required
    python guardian.py --purge                # empty Recycle Bin permanently
    python guardian.py --check-rules          # validate rules + deny verdicts
"""
from __future__ import annotations

import argparse
import ctypes
import datetime as dt
import json
import os
import re
import shutil
import stat
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))
import recycle  # noqa: E402  (local module, deliberately after sys.path fix)

RULES_FILE = ROOT / "rules.json"
OUT = ROOT / "out"
ACTION_LOG = OUT / "actions.jsonl"
REPORT_FILE = OUT / "last_report.json"

WARN_TEXT = "此操作非常危险，可能导致不可逆的数据丢失！"
REPARSE_POINT = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)

# Files that must never be a deletion target, whatever the rule table says.
PROTECTED_BASENAMES = {
    "pagefile.sys", "swapfile.sys", "hiberfil.sys",
    "dedicateddumpfile.sys", "memory.dmp", "ntuser.dat",
}

# HARD_DENY, part 1: the whole subtree is off limits. A rule may only point
# inside one of these if it has allow_inside_deny=true AND level >= 3.
DENY_SUBTREE_TEMPLATES = [
    r"%SystemRoot%\assembly",           # NGEN images; clearing triggers a ~2.5 GiB rebuild
    r"%SystemRoot%\WinSxS",             # breaks update uninstall / SFC
    r"%SystemRoot%\Installer",          # breaks MSI repair / uninstall
    r"%SystemRoot%\winevt",             # blue-screen evidence lives here
    r"%SystemRoot%\System32",
    r"%SystemRoot%\SysWOW64",
    r"%SystemRoot%\Boot",
    r"%SystemRoot%\Fonts",
    r"%SystemRoot%\MEMORY.DMP",
    r"%SystemRoot%\Minidump",
    r"%ProgramFiles%",
    r"%ProgramFiles(x86)%",
    r"%USERPROFILE%\Documents",
    r"%USERPROFILE%\Desktop",
    r"%USERPROFILE%\Pictures",
    r"%USERPROFILE%\.workbuddy",        # sessions / plugins / runtime as a whole
    r"%USERPROFILE%\.ssh",
    r"%USERPROFILE%\.gnupg",
    r"%LOCALAPPDATA%\CrashDumps",
    r"%LOCALAPPDATA%\Microsoft\Edge\User Data",  # bookmarks / passwords / history
    "C:\\$Recycle.Bin",                 # only --purge may touch this
    "C:\\Windows.old",
]

# HARD_DENY, part 2: these exact paths must not be deleted, and no target may
# be an ANCESTOR of them (deleting the parent would wipe protected children).
DENY_EXACT_TEMPLATES = [
    r"%USERPROFILE%",
    r"%LOCALAPPDATA%",
    r"%APPDATA%",
    r"%SystemRoot%",
    r"%ProgramData%",
    r"%CODE_ROOT%",
    "C:\\",
    "D:\\",
    "C:\\pagefile.sys",
    "C:\\swapfile.sys",
    "C:\\hiberfil.sys",
    "D:\\pagefile.sys",
    "D:\\dedicateddumpfile.sys",
]

VAR_RE = re.compile(r"%([^%]+)%")

# Synthetic targets used by --check-rules. Each one MUST be rejected. If any of
# these ever comes back clean, HARD_DENY is broken and the tool refuses to run.
SELF_TEST_MUST_BLOCK = [
    r"%SystemRoot%\WinSxS",
    r"%SystemRoot%\assembly",
    r"%SystemRoot%\winevt\Logs",
    r"%SystemRoot%\System32",
    r"%SystemRoot%",
    r"%USERPROFILE%",
    r"%USERPROFILE%\Documents",
    r"%USERPROFILE%\Documents\anything.txt",
    r"%USERPROFILE%\.workbuddy",
    r"%LOCALAPPDATA%\CrashDumps",
    r"%ProgramFiles%",
    r"%LOCALAPPDATA%\Microsoft\Edge\User Data",
    "C:\\",
    "C:\\pagefile.sys",
    "D:\\dedicateddumpfile.sys",
]
SELF_TEST_MUST_PASS = [
    r"%LOCALAPPDATA%\NVIDIA\DXCache",
    r"%LOCALAPPDATA%\go-build",
    r"%SystemRoot%\Temp",
]


def out(msg: str = "") -> None:
    """Print with fallbacks for cp936 consoles and pythonw (no stdout at all)."""
    if sys.stdout is None:  # pythonw.exe: the GUI server has no console
        return
    try:
        print(msg, flush=True)
    except UnicodeEncodeError:
        try:
            print(msg.encode("ascii", "backslashreplace").decode("ascii"), flush=True)
        except Exception:
            pass
    except Exception:
        pass


def build_vars(rules_doc: dict) -> dict:
    env = os.environ
    v = {
        "USERPROFILE": env.get("USERPROFILE", ""),
        "LOCALAPPDATA": env.get("LOCALAPPDATA", ""),
        "APPDATA": env.get("APPDATA", ""),
        "TEMP": env.get("TEMP", ""),
        "SystemRoot": env.get("SystemRoot", "C:\\Windows"),
        "ProgramData": env.get("ProgramData", "C:\\ProgramData"),
        "ProgramFiles": env.get("ProgramFiles", "C:\\Program Files"),
        "ProgramFiles(x86)": env.get("ProgramFiles(x86)", "C:\\Program Files (x86)"),
    }
    v.update(rules_doc.get("vars", {}))
    return v


def norm(path: str) -> str:
    return os.path.normcase(os.path.normpath(os.path.abspath(path)))


def expand(template: str, variables: dict) -> str:
    def repl(match):
        key = match.group(1)
        if key not in variables:
            raise ValueError("unknown variable %%%s%% in %r" % (key, template))
        return variables[key]
    return norm(VAR_RE.sub(repl, template))


def is_ancestor(parent: str, child: str) -> bool:
    p, c = norm(parent), norm(child)
    if p == c:
        return True
    return c.startswith(p.rstrip("\\") + "\\")


def is_admin() -> bool:
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def human(num: float) -> str:
    if num >= 2 ** 30:
        return "%.2f GiB" % (num / 2 ** 30)
    if num >= 2 ** 20:
        return "%.1f MiB" % (num / 2 ** 20)
    if num >= 2 ** 10:
        return "%.0f KiB" % (num / 2 ** 10)
    return "%d B" % num


def free_gib(drive: str = "C:\\"):
    u = shutil.disk_usage(drive)
    return u.free / 2 ** 30, u.total / 2 ** 30


def _is_reparse_st(st) -> bool:
    """Junctions / symlinks must be skipped or a scan can loop forever.

    Uses the stat we already have from DirEntry -- a separate os.stat() per
    entry doubled the syscall count and made an L2 scan (300k files) take
    longer than the GUI request timeout (measured 2026-11-07).
    """
    return bool(getattr(st, "st_file_attributes", 0) & REPARSE_POINT)


def scan_tree(path: str) -> dict:
    info = {"bytes": 0, "files": 0, "dirs": 0, "newest": 0, "errors": 0}
    stack = [path]
    while stack:
        cur = stack.pop()
        try:
            with os.scandir(cur) as it:
                for entry in it:
                    try:
                        st = entry.stat(follow_symlinks=False)
                        if _is_reparse_st(st):
                            continue
                        if entry.is_dir(follow_symlinks=False):
                            info["dirs"] += 1
                            stack.append(entry.path)
                        else:
                            info["bytes"] += st.st_size
                            info["files"] += 1
                            info["newest"] = max(info["newest"], st.st_mtime)
                    except OSError:
                        info["errors"] += 1
        except OSError:
            info["errors"] += 1
    return info


def collect_old_files(paths, keep_days: int):
    """For mode=old-files: keep the directories, only pick stale files."""
    cutoff = time.time() - keep_days * 86400
    found = []
    for root in paths:
        if not os.path.isdir(root):
            continue
        stack = [root]
        while stack:
            cur = stack.pop()
            try:
                with os.scandir(cur) as it:
                    for entry in it:
                        try:
                            st = entry.stat(follow_symlinks=False)
                            if _is_reparse_st(st):
                                continue
                            if entry.is_dir(follow_symlinks=False):
                                stack.append(entry.path)
                            elif st.st_mtime < cutoff:
                                found.append(entry.path)
                        except OSError:
                            pass
            except OSError:
                pass
    return found


# Discovery: a bare directory name is never enough evidence to delete it.
# `target` could be anything; a sibling Cargo.toml makes it a build dir.
# One walk serves every rule that shares the same roots (measured: two rules
# with the same roots cost 12.6 s; sharing the index halves it).
_DIR_INDEX_CACHE = {}


def _dir_index(roots, max_depth: int, dirnames: frozenset) -> list:
    """Walk `roots` once and collect (lowercased name, path) for `dirnames`.

    Prunes on a match: a `node_modules` never contains another one worth
    looking at, and descending into it costs ~200k stat calls for nothing.
    """
    key = (tuple(roots), max_depth, dirnames)
    cached = _DIR_INDEX_CACHE.get(key)
    if cached is not None:
        return cached
    hits = []

    def walk(parent: str, depth: int) -> None:
        try:
            with os.scandir(parent) as it:
                entries = list(it)
        except OSError:
            return
        for entry in entries:
            # is_dir() is answered from the scandir buffer; stat() is a real
            # syscall. On D: this loop sees ~1.1M entries, so stat-ing every
            # file made discovery 3x slower for no benefit (2026-11-07).
            try:
                if not entry.is_dir(follow_symlinks=False):
                    continue
                st = entry.stat(follow_symlinks=False)
            except OSError:
                continue
            if _is_reparse_st(st):
                continue
            name = entry.name.lower()
            if name in dirnames:
                hits.append((name, entry.path))
                continue  # a matched dir never nests another one
            if depth < max_depth:
                walk(entry.path, depth + 1)

    for root in roots:
        if os.path.isdir(root):
            walk(root, 0)
    _DIR_INDEX_CACHE[key] = hits
    return hits


def _filter_hits(index, dirname: str, sibling: str, excludes) -> list:
    """Apply the identity checks to raw index hits."""
    picked = []
    for name, path in index:
        if name != dirname:
            continue
        low = path.lower()
        # A sibling manifest is NOT enough on its own: every Electron app ships
        # resources/app/package.json + node_modules, and those are the program
        # itself. Deleting them breaks the installed app (measured 2026-11-07:
        # Cursor / Photoshop / WorkBuddy / WeChat devtools all matched).
        if any(x in low for x in excludes):
            continue
        if sibling and not os.path.isfile(os.path.join(os.path.dirname(path), sibling)):
            continue
        picked.append(path)
    picked.sort()
    return picked


def _spec_parts(spec: dict, variables: dict):
    dirname = str(spec.get("dirname", "")).strip().lower()
    roots = [expand(r, variables) for r in spec.get("roots", [])]
    return {
        "dirname": dirname,
        "roots": roots,
        "sibling": str(spec.get("require_sibling", "") or ""),
        "max_depth": int(spec.get("max_depth", 5)),
        "excludes": [str(x).lower() for x in spec.get("exclude_contains", [])],
    }


def discover_paths(spec: dict, variables: dict) -> list:
    """Find disposable dirs by name under given roots, guarded by a sibling file.

    rules.json cannot hardcode every project path -- D: holds 8 Rust targets and
    39 node_modules today, and the set changes weekly. Hardcoding rots silently;
    a name-only match is dangerous. So each hit must prove its identity with a
    sibling manifest (Cargo.toml / package.json) and must not sit inside an
    application bundle.
    """
    parts = _spec_parts(spec, variables)
    if not parts["dirname"]:
        return []
    index = _dir_index(parts["roots"], parts["max_depth"], frozenset([parts["dirname"]]))
    return _filter_hits(index, parts["dirname"], parts["sibling"], parts["excludes"])


class Logger:
    """Line-buffered log: every line is flushed AND fsynced immediately."""

    def __init__(self, path: Path):
        OUT.mkdir(parents=True, exist_ok=True)
        self.path = path
        self.fh = open(path, "a", encoding="utf-8")

    def __call__(self, msg: str = ""):
        line = "[%s] %s" % (dt.datetime.now().strftime("%H:%M:%S"), msg) if msg else ""
        out(line)
        self.fh.write(line + "\n")
        self.fh.flush()
        os.fsync(self.fh.fileno())

    def close(self):
        self.fh.close()


def log_action(record: dict):
    """Append-only action journal, one JSON object per line, flushed at once."""
    OUT.mkdir(parents=True, exist_ok=True)
    record["ts"] = dt.datetime.now().isoformat(timespec="seconds")
    with open(ACTION_LOG, "a", encoding="utf-8") as fh:
        fh.write(json.dumps(record, ensure_ascii=False) + "\n")
        fh.flush()
        os.fsync(fh.fileno())


def confirm(token: str, assume_yes: bool) -> bool:
    if assume_yes:
        return True
    if sys.stdin is None or not sys.stdin.isatty():
        out("Refusing: non-interactive session. Pass --yes only if you really mean it.")
        return False
    try:
        return input("Type %s to continue: " % token).strip() == token
    except (EOFError, KeyboardInterrupt):
        return False


def check_target(rule: dict, path: str, deny_subtree, deny_exact):
    """Return a list of reasons why this path must not be deleted."""
    reasons = []
    parts = path.split(os.sep)
    if len(parts) < 3 or not os.path.splitdrive(path)[0]:
        reasons.append("路径层级过浅或不是绝对路径")
    if parts[-1].lower() in PROTECTED_BASENAMES:
        reasons.append("命中系统关键文件名保护")
    for d in deny_exact + deny_subtree:
        if is_ancestor(path, d):
            reasons.append("删它会连带删掉受保护项 %s" % d)
            break
    if not (rule.get("allow_inside_deny") and int(rule["level"]) >= 3):
        for d in deny_subtree:
            if is_ancestor(d, path):
                reasons.append("位于硬禁区 %s 之内" % d)
                break
    return reasons


def load_rules():
    with open(RULES_FILE, "r", encoding="utf-8") as fh:
        doc = json.load(fh)
    variables = build_vars(doc)
    seen = set()
    for r in doc.get("rules", []):
        rid = r.get("id", "")
        if not re.fullmatch(r"[a-z0-9][a-z0-9-]*", rid):
            raise SystemExit("rules.json: invalid rule id %r" % rid)
        if rid in seen:
            raise SystemExit("rules.json: duplicate rule id %r" % rid)
        seen.add(rid)
        if r.get("level") not in (1, 2, 3):
            raise SystemExit("rules.json: %s has invalid level %r" % (rid, r.get("level")))
        if not r.get("paths") and not r.get("discover"):
            raise SystemExit("rules.json: %s has neither paths nor discover" % rid)
        for p in r.get("paths", []):
            try:
                expand(p, variables)
            except ValueError as exc:
                raise SystemExit("rules.json: %s: %s" % (rid, exc))
        spec = r.get("discover")
        if spec is not None:
            if not isinstance(spec, dict) or not spec.get("dirname"):
                raise SystemExit("rules.json: %s discover needs a dirname" % rid)
            for root in spec.get("roots", []):
                try:
                    expand(root, variables)
                except ValueError as exc:
                    raise SystemExit("rules.json: %s: %s" % (rid, exc))
    return doc, variables, seen


def discover_selftest(logger=None) -> bool:
    """Fail fast if discovery can mistake a program's bundled deps for a project's.

    Regression guard for 2026-11-07: the first version of the node_modules rule
    matched `D:\\Tools\\Cursor\\resources\\app\\node_modules` and
    `...\\Adobe Photoshop 2025\\Required\\...\\node_modules`. A sibling
    package.json exists in every Electron app, so the manifest check alone is
    not sufficient -- the app would have been destroyed.
    """
    import tempfile
    ok = True
    with tempfile.TemporaryDirectory() as td:
        proj = os.path.join(td, "proj")
        os.makedirs(os.path.join(proj, "node_modules"))
        open(os.path.join(proj, "package.json"), "w").close()

        app = os.path.join(td, "Editor", "resources", "app")
        os.makedirs(os.path.join(app, "node_modules"))
        open(os.path.join(app, "package.json"), "w").close()

        os.makedirs(os.path.join(td, "bare", "node_modules"))

        spec = {
            "roots": [td],
            "dirname": "node_modules",
            "require_sibling": "package.json",
            "max_depth": 6,
            "exclude_contains": ["\\resources\\app"],
        }
        found = [p.lower() for p in discover_paths(spec, {})]
        cases = [
            ("项目依赖被发现",
             any(p.endswith("proj\\node_modules") for p in found), True),
            ("Electron 应用被排除",
             any("resources\\app" in p for p in found), False),
            ("无包清单的目录被跳过",
             any(p.endswith("bare\\node_modules") for p in found), False),
        ]
        for name, actual, want in cases:
            if actual != want:
                ok = False
                if logger:
                    logger("SELF-TEST FAIL: 发现器 %s（实际 %s）" % (name, actual))
            elif logger:
                logger("SELF-TEST PASS  discover  %s" % name)
    return ok


def deny_selftest(variables, logger=None):
    """Fail fast if HARD_DENY has a hole. Returns True when all assertions hold."""
    deny_subtree = [expand(t, variables) for t in DENY_SUBTREE_TEMPLATES]
    deny_exact = [expand(t, variables) for t in DENY_EXACT_TEMPLATES]
    plain = {"level": 1}
    ok = True
    for template in SELF_TEST_MUST_BLOCK:
        p = expand(template, variables)
        reasons = check_target(plain, p, deny_subtree, deny_exact)
        if not reasons:
            ok = False
            if logger:
                logger("SELF-TEST FAIL: 硬禁区没拦住 %s" % p)
        elif logger:
            logger("SELF-TEST PASS  blocked  %-58s %s" % (p, reasons[0]))
    for template in SELF_TEST_MUST_PASS:
        p = expand(template, variables)
        reasons = check_target(plain, p, deny_subtree, deny_exact)
        if reasons:
            ok = False
            if logger:
                logger("SELF-TEST FAIL: 正常缓存被误拦 %s -> %s" % (p, reasons[0]))
        elif logger:
            logger("SELF-TEST PASS  allowed  %s" % p)
    if not discover_selftest(logger):
        ok = False
    return ok


def _discover_all(rules_doc, variables, level, include, exclude) -> dict:
    """Run discovery for every rule, sharing one walk per (roots, depth) group.

    Only rules that can actually enter the plan are discovered -- otherwise an
    L1 scan pays for D-drive discovery of level-2 rules (measured: 1.1 s -> 5.4 s).
    """
    groups = {}
    for rule in rules_doc["rules"]:
        if int(rule["level"]) > level:
            continue
        if include and rule["id"] not in include:
            continue
        if rule["id"] in exclude:
            continue
        spec = rule.get("discover")
        if not spec:
            continue
        parts = _spec_parts(spec, variables)
        if not parts["dirname"]:
            continue
        key = (tuple(parts["roots"]), parts["max_depth"])
        groups.setdefault(key, []).append((rule["id"], parts))
    result = {}
    for (roots, md), items in groups.items():
        names = frozenset(p["dirname"] for _, p in items)
        index = _dir_index(list(roots), md, names)
        for rid, parts in items:
            result[rid] = _filter_hits(index, parts["dirname"],
                                       parts["sibling"], parts["excludes"])
    return result


def build_plan(rules_doc, variables, level, include, exclude, admin):
    deny_subtree = [expand(t, variables) for t in DENY_SUBTREE_TEMPLATES]
    deny_exact = [expand(t, variables) for t in DENY_EXACT_TEMPLATES]
    discovered = _discover_all(rules_doc, variables, level, include, exclude)
    plan = []
    for rule in rules_doc["rules"]:
        if int(rule["level"]) > level:
            continue
        if include and rule["id"] not in include:
            continue
        if rule["id"] in exclude:
            continue
        paths = [expand(p, variables) for p in rule.get("paths", [])]
        if rule.get("discover"):
            # Discovery results are ordinary targets from here on: they go
            # through the same HARD_DENY check as any hardcoded path.
            paths.extend(discovered.get(rule["id"], []))
        reasons = []
        for p in paths:
            reasons.extend(check_target(rule, p, deny_subtree, deny_exact))
        existing = [p for p in paths if os.path.exists(p)]
        # Measured 2026-11-07: a 4-thread pool made this SLOWER (11.2->11.9 s
        # for L2, 13.9->17.4 s for L3) -- one SSD, concurrent scandir just
        # contends. Keep it serial; the win was removing the duplicate stat.
        scans = [scan_tree(p) for p in existing]
        size = sum(s["bytes"] for s in scans)
        files = sum(s["files"] for s in scans)
        newest = max((s["newest"] for s in scans), default=0)
        errors = sum(s["errors"] for s in scans)

        if reasons:
            status = "BLOCKED"
        elif not existing:
            status = "missing"
        elif rule.get("requires_admin") and not admin:
            status = "need-admin"
        elif rule.get("recent_hours") and newest > time.time() - rule["recent_hours"] * 3600:
            status = "recent"
        elif size == 0:
            status = "empty"
        else:
            status = "ready"

        plan.append({
            "id": rule["id"],
            "name": rule.get("name", rule["id"]),
            "level": int(rule["level"]),
            "paths": paths,
            "existing": existing,
            "size_bytes": size,
            "size": human(size),
            "files": files,
            "errors": errors,
            "status": status,
            "reasons": reasons,
            "rebuild": rule.get("rebuild", ""),
            "rebuild_cmd": rule.get("rebuild_cmd", ""),
            "note": rule.get("note", ""),
            "mode": rule.get("mode", "dir"),
            "keep_days": int(rule.get("keep_days", 14)),
            "_rule": rule,
        })
    return plan


def print_plan(plan, level, logger, check_rules=False):
    c_free, c_total = free_gib("C:\\")
    logger("disk-guardian  level<=%d  |  C: free %.2f / %.2f GiB  |  admin=%s" %
           (level, c_free, c_total, is_admin()))
    logger("HARD_DENY 硬禁区 %d 条（写在 guardian.py 里，规则表改不动）" %
           (len(DENY_SUBTREE_TEMPLATES) + len(DENY_EXACT_TEMPLATES)))
    logger("")
    logger("%-4s  %-22s %11s %8s  %-11s %s" %
           ("LV", "ID", "SIZE", "FILES", "STATUS", "NAME"))
    logger("-" * 100)
    total = 0
    for x in plan:
        logger("%-4d  %-22s %11s %8d  %-11s %s" %
               (x["level"], x["id"], x["size"], x["files"], x["status"], x["name"]))
        if x["status"] == "ready":
            total += x["size_bytes"]
        for p in x["paths"]:
            logger("       %s" % p)
        for reason in x["reasons"]:
            logger("       BLOCKED: %s" % reason)
        if x["status"] == "recent":
            logger("       近 %s 小时内有写入，自动跳过" % x["_rule"].get("recent_hours"))
        if x["note"]:
            logger("       注: %s" % x["note"])
        if x["rebuild"]:
            logger("       重建: %s" % x["rebuild"])
        if x["rebuild_cmd"]:
            logger("       更稳的命令: %s" % x["rebuild_cmd"])
        if x["errors"]:
            logger("       扫描时有 %d 个条目读不到（占用/权限），仅影响统计" % x["errors"])
    logger("-" * 100)
    logger("可清理合计 %s（仅统计 status=ready）" % human(total))
    if check_rules:
        logger("本次为 --check-rules：只校验规则与硬禁区，不执行任何删除。")
    return total


def expected_token(level: int) -> str:
    return "DELETE" if level == 1 else "DELETE%d" % level


def execute_rules(plan, ids, level, logger, preconfirmed=False, assume_yes=False):
    """The ONE execution path, shared by the CLI and the GUI server.

    The caller may only pass rule ids -- never paths. Every id is re-checked
    against a freshly built plan: it must exist, be within the level gate,
    have status=ready and carry no HARD_DENY verdict. HARD_DENY itself is
    re-asserted before the first deletion.

    preconfirmed=True is for the GUI, which has already collected and verified
    the confirmation token itself (the server re-verifies it before calling).
    """
    rules_doc, variables, _ = load_rules()
    if not deny_selftest(variables):
        logger("拒绝执行：HARD_DENY 自测未通过。")
        return {"ok": 0, "failed": 0, "error": "deny self-test failed"}

    by_id = {x["id"]: x for x in plan}
    unknown = [i for i in ids if i not in by_id]
    if unknown:
        logger("拒绝执行：未知规则 id %s" % ", ".join(unknown))
        return {"ok": 0, "failed": 0, "error": "unknown ids"}

    selected = [by_id[i] for i in ids]
    blocked = [x["id"] for x in selected
               if x["level"] > level or x["status"] != "ready" or x["reasons"]]
    if blocked:
        logger("拒绝执行：以下规则未通过等级/状态/禁区复核 %s" % ", ".join(blocked))
        return {"ok": 0, "failed": 0, "error": "rules not eligible"}
    if not selected:
        logger("没有选中任何规则，取消执行。")
        return {"ok": 0, "failed": 0, "error": "nothing selected"}

    logger("")
    logger(WARN_TEXT)
    logger("即将把下列内容【移入回收站】（可撤销，暂不永久删除）：")
    for x in selected:
        logger("  [%d] %-22s %11s  %s" % (x["level"], x["id"], x["size"], x["name"]))
        for p in x["existing"]:
            logger("       %s" % p)
    logger("真正腾出空间需要另跑 --purge；本次只是移入回收站。")
    token = expected_token(level)
    if not preconfirmed and not confirm(token, assume_yes):
        logger("未确认，未删除任何东西。")
        return {"ok": 0, "failed": 0, "error": "not confirmed"}

    before, _ = free_gib("C:\\")
    ok_total, fail_total, details = 0, 0, []
    for x in selected:
        if x["mode"] == "old-files":
            targets = collect_old_files(x["existing"], x["keep_days"])
            logger("%s: old-files 模式，%d 个 %d 天前的文件" %
                   (x["id"], len(targets), x["keep_days"]))
        else:
            targets = list(x["existing"])
        if not targets:
            logger("%s: 没有目标，跳过" % x["id"])
            continue
        ok, failed = recycle.send_many(targets, permanent=False)
        ok_total += len(ok)
        fail_total += len(failed)
        logger("%s: ok=%d failed=%d" % (x["id"], len(ok), len(failed)))
        for p, rc in failed[:5]:
            logger("   FAILED rc=%s %s" % (rc, p))
        # Flush the journal BEFORE moving on to the next rule.
        log_action({
            "action": "apply",
            "rule": x["id"],
            "level": x["level"],
            "mode": x["mode"],
            "paths": targets,
            "size_bytes": x["size_bytes"],
            "ok": len(ok),
            "failed": [{"path": p, "rc": rc} for p, rc in failed],
        })
        details.append({"id": x["id"], "ok": len(ok), "failed": len(failed)})
    after, _ = free_gib("C:\\")
    logger("")
    logger("执行完毕: ok=%d failed=%d | C: free %.2f -> %.2f GiB (delta %+.2f)" %
           (ok_total, fail_total, before, after, after - before))
    logger("下一步：确认无误后跑 --purge 永久清空回收站（会再次确认）。")
    return {"ok": ok_total, "failed": fail_total, "details": details,
            "free_before": round(before, 3), "free_after": round(after, 3)}


def do_apply(plan, level, logger, assume_yes):
    ids = [x["id"] for x in plan if x["status"] == "ready"]
    result = execute_rules(plan, ids, level, logger,
                           preconfirmed=False, assume_yes=assume_yes)
    return 0 if "error" not in result else 2


def do_purge(drives, logger, assume_yes):
    roots = [d.strip().upper() + ":\\" for d in drives.split(",") if d.strip()]
    items = recycle.list_items(roots)
    total = sum(i[0] for i in items)
    logger("")
    logger("回收站条目 %d 个，合计 %s（盘符: %s）" % (len(items), human(total), ", ".join(roots)))
    for size, original, _, _ in sorted(items, reverse=True)[:15]:
        logger("   %11s  %s" % (human(size), original))
    logger(WARN_TEXT)
    logger("这一步是【永久删除】，不可撤销；清单如上。")
    if not confirm("PURGE", assume_yes):
        logger("未确认，回收站未动。")
        return 2
    ok, failed = recycle.empty_safely(roots)
    logger("清空完成: ok=%d failed=%d" % (ok, len(failed)))
    for p, rc in failed[:10]:
        logger("   FAILED rc=%s %s" % (rc, p))
    log_action({"action": "purge", "drives": roots, "ok": ok,
                "failed": [{"path": p, "rc": rc} for p, rc in failed]})
    after, _ = free_gib("C:\\")
    logger("C: free = %.2f GiB" % after)
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(
        description="disk-guardian: rule-driven, preview-first disk cleanup")
    ap.add_argument("--level", type=int, choices=[1, 2, 3], default=1,
                    help="include rules with level <= N (default 1). Level 3 needs --level 3 explicitly")
    ap.add_argument("--apply", action="store_true", help="move selected rules to the Recycle Bin")
    ap.add_argument("--purge", action="store_true", help="permanently empty the Recycle Bin")
    ap.add_argument("--yes", action="store_true",
                    help="skip the interactive confirmation (only for scripted runs)")
    ap.add_argument("--include", default="", help="comma-separated rule ids; still gated by --level")
    ap.add_argument("--exclude", default="", help="comma-separated rule ids to skip")
    ap.add_argument("--drives", default="C", help="drives for --purge, e.g. C,D (default C)")
    ap.add_argument("--json-report", action="store_true", help="write out/last_report.json")
    ap.add_argument("--check-rules", action="store_true",
                    help="validate rules.json and print HARD_DENY verdicts, no changes")
    ap.add_argument("--list", action="store_true", help="list rule ids and levels")
    args = ap.parse_args(argv)

    if args.apply and args.purge:
        raise SystemExit("--apply 和 --purge 请分开跑，避免一次做两件破坏性动作。")

    rules_doc, variables, known = load_rules()
    # Fail closed: if HARD_DENY has a hole, do not even offer a preview.
    if not deny_selftest(variables):
        raise SystemExit("HARD_DENY self-test failed. Refusing to run.")
    if args.list:
        for r in rules_doc["rules"]:
            out("  %d  %-22s %s" % (r["level"], r["id"], r.get("name", "")))
        return 0

    include = {x.strip() for x in args.include.split(",") if x.strip()}
    exclude = {x.strip() for x in args.exclude.split(",") if x.strip()}
    unknown = (include | exclude) - known
    if unknown:
        raise SystemExit("unknown rule id(s): %s" % ", ".join(sorted(unknown)))
    too_high = [i for i in include
                if next(r for r in rules_doc["rules"] if r["id"] == i)["level"] > args.level]
    if too_high:
        raise SystemExit("这些规则等级高于 --level %d，请显式提高 --level：%s" %
                         (args.level, ", ".join(too_high)))

    admin = is_admin()
    logger = Logger(OUT / ("run_%s.log" % dt.datetime.now().strftime("%Y%m%d_%H%M%S")))
    logger("=== disk-guardian start | mode=%s | level<=%d ===" %
           ("check" if args.check_rules else "purge" if args.purge else
            "apply" if args.apply else "preview", args.level))
    if args.check_rules:
        logger("--- HARD_DENY self-test ---")
        deny_selftest(variables, logger)
        logger("--- end self-test ---")
    plan = build_plan(rules_doc, variables, args.level, include, exclude, admin)
    total = print_plan(plan, args.level, logger, args.check_rules)

    rc = 0
    if args.apply:
        rc = do_apply(plan, args.level, logger, args.yes)
    elif args.purge:
        rc = do_purge(args.drives, logger, args.yes)
    elif not args.check_rules:
        logger("")
        logger("这是只读预览，没有删除任何东西。加 --apply 才动手，加 --level 2/3 才纳入需确认项。")

    if args.json_report:
        report = {
            "generated_at": dt.datetime.now().isoformat(timespec="seconds"),
            "mode": "check" if args.check_rules else "purge" if args.purge else
                    "apply" if args.apply else "preview",
            "level": args.level,
            "is_admin": admin,
            "c_free_gib": round(free_gib("C:\\")[0], 2),
            "ready_bytes": total,
            "rules": [{k: v for k, v in x.items() if k != "_rule"} for x in plan],
        }
        OUT.mkdir(parents=True, exist_ok=True)
        with open(REPORT_FILE, "w", encoding="utf-8") as fh:
            json.dump(report, fh, ensure_ascii=False, indent=2)
        logger("报告: %s" % REPORT_FILE)

    logger("=== done rc=%d ===" % rc)
    logger.close()
    return rc


if __name__ == "__main__":
    sys.exit(main())
