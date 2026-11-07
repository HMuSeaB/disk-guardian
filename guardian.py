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
    """Print with a fallback so a cp936 console never crashes on odd chars."""
    try:
        print(msg, flush=True)
    except UnicodeEncodeError:
        print(msg.encode("ascii", "backslashreplace").decode("ascii"), flush=True)


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


def _is_reparse(path: str) -> bool:
    """Skip junctions / symlinks so a scan never escapes into a loop."""
    try:
        st = os.stat(path, follow_symlinks=False)
        return bool(getattr(st, "st_file_attributes", 0) & REPARSE_POINT)
    except OSError:
        return True  # conservative: unreadable -> do not descend


def scan_tree(path: str) -> dict:
    info = {"bytes": 0, "files": 0, "dirs": 0, "newest": 0, "errors": 0}
    stack = [path]
    while stack:
        cur = stack.pop()
        try:
            with os.scandir(cur) as it:
                for entry in it:
                    try:
                        if _is_reparse(entry.path):
                            continue
                        if entry.is_dir(follow_symlinks=False):
                            info["dirs"] += 1
                            stack.append(entry.path)
                        else:
                            st = entry.stat(follow_symlinks=False)
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
                            if _is_reparse(entry.path):
                                continue
                            if entry.is_dir(follow_symlinks=False):
                                stack.append(entry.path)
                            elif entry.stat(follow_symlinks=False).st_mtime < cutoff:
                                found.append(entry.path)
                        except OSError:
                            pass
            except OSError:
                pass
    return found


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
    if not sys.stdin.isatty():
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
        if not r.get("paths"):
            raise SystemExit("rules.json: %s has no paths" % rid)
        for p in r["paths"]:
            try:
                expand(p, variables)
            except ValueError as exc:
                raise SystemExit("rules.json: %s: %s" % (rid, exc))
    return doc, variables, seen


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
    return ok


def build_plan(rules_doc, variables, level, include, exclude, admin):
    deny_subtree = [expand(t, variables) for t in DENY_SUBTREE_TEMPLATES]
    deny_exact = [expand(t, variables) for t in DENY_EXACT_TEMPLATES]
    plan = []
    for rule in rules_doc["rules"]:
        if int(rule["level"]) > level:
            continue
        if include and rule["id"] not in include:
            continue
        if rule["id"] in exclude:
            continue
        paths = [expand(p, variables) for p in rule["paths"]]
        reasons = []
        for p in paths:
            reasons.extend(check_target(rule, p, deny_subtree, deny_exact))
        existing = [p for p in paths if os.path.exists(p)]
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


def do_apply(plan, level, logger, assume_yes):
    ready = [x for x in plan if x["status"] == "ready"]
    if not ready:
        logger("没有 status=ready 的规则，取消执行。")
        return 1
    logger("")
    logger(WARN_TEXT)
    logger("即将把下列内容【移入回收站】（可撤销，暂不永久删除）：")
    for x in ready:
        logger("  [%d] %-22s %11s  %s" % (x["level"], x["id"], x["size"], x["name"]))
        for p in x["existing"]:
            logger("       %s" % p)
    logger("真正腾出空间需要另跑 --purge；本次只是移入回收站。")
    token = "DELETE" if level == 1 else "DELETE%d" % level
    if not confirm(token, assume_yes):
        logger("未确认，未删除任何东西。")
        return 2

    before, _ = free_gib("C:\\")
    ok_total, fail_total = 0, 0
    for x in ready:
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
    after, _ = free_gib("C:\\")
    logger("")
    logger("执行完毕: ok=%d failed=%d | C: free %.2f -> %.2f GiB (delta %+.2f)" %
           (ok_total, fail_total, before, after, after - before))
    logger("下一步：确认无误后跑 --purge 永久清空回收站（会再次确认）。")
    return 0


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
