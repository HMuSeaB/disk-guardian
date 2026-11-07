#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""disk-guardian GUI server.

A loopback-only HTTP service rendered inside an Edge/Chrome `--app=` window,
so it looks and behaves like a local program rather than a web page.

Hard constraints (from the go-embedded-webui-desktop playbook):
  * listen on 127.0.0.1 only -- a non-loopback address is REJECTED, not warned
  * one-shot token; the HTML entry point checks it too (?t=), API uses a header
  * no idle auto-exit (background tabs get frozen); exit is /api/quit or the UI
  * CLI and GUI share guardian.execute_rules -- no second copy of the logic
  * the browser only ever sends rule ids, never paths

Verification: start with --no-open --addr 127.0.0.1:PORT, read out/gui_run.json
for the actual addr/token, then POST /api/quit. Do all of it in one tool call:
an agent-spawned process does not survive the tool-call boundary.
"""
from __future__ import annotations

import argparse
import hmac
import ipaddress
import json
import os
import secrets
import shutil
import subprocess
import sys
import threading
import time
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

ROOT = Path(__file__).resolve().parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))
import guardian  # noqa: E402
import recycle  # noqa: E402

ASSETS = Path(__file__).resolve().parent / "assets"
INDEX = ASSETS / "index.html"
RUN_MARK = guardian.OUT / "gui_run.json"
TOKEN_HEADER = "X-DG-Token"
MAX_BODY = 1024 * 1024

RULE_FIELDS = (
    "id", "name", "level", "paths", "existing", "size", "size_bytes",
    "files", "errors", "status", "reasons", "rebuild", "rebuild_cmd",
    "note", "mode", "keep_days",
)


def rule_view(x: dict) -> dict:
    """Serializable plan row. Drops _rule so it can go straight to JSON."""
    return {k: x[k] for k in RULE_FIELDS if k in x}


def drive_info(letter: str):
    try:
        u = shutil.disk_usage(letter + ":\\")
        return {
            "letter": letter,
            "free": round(u.free / 2 ** 30, 2),
            "total": round(u.total / 2 ** 30, 2),
            "used": round(u.used / 2 ** 30, 2),
            "pct": round(u.used / u.total * 100, 1),
        }
    except OSError:
        return None


class App:
    def __init__(self, token: str):
        self.token = token
        guardian.OUT.mkdir(parents=True, exist_ok=True)
        self.logger = guardian.Logger(
            guardian.OUT / ("gui_%s.log" % datetime.now().strftime("%Y%m%d_%H%M%S")))
        self._cache = {}
        self._lock = threading.Lock()

    def plan(self, level: int, refresh: bool = False):
        now = time.time()
        with self._lock:
            cached = self._cache.get(level)
            # A full L3 scan is ~14 s on this machine (480k files). Keep the
            # result around so switching back and forth is instant; execution
            # always calls with refresh=True against a fresh plan.
            if not refresh and cached and now - cached[0] < 300:
                return cached[1]
        doc, variables, _ = guardian.load_rules()
        rows = [rule_view(x) for x in
                guardian.build_plan(doc, variables, level, set(), set(),
                                    guardian.is_admin())]
        with self._lock:
            self._cache[level] = (now, rows)
        return rows

    def invalidate(self):
        with self._lock:
            self._cache.clear()


class Handler(BaseHTTPRequestHandler):
    server_version = "disk-guardian"
    sys_version = ""

    def log_message(self, fmt, *args):
        pass  # we keep our own flushed log; do not spam stderr

    # ---------- low level ----------

    def _send(self, code: int, data: bytes, ctype: str):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("X-Frame-Options", "DENY")
        self.send_header("Content-Security-Policy",
                         "default-src 'self' 'unsafe-inline'; connect-src 'self'; "
                         "img-src 'self' data:; frame-ancestors 'none'")
        self.end_headers()
        try:
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def _json(self, code: int, obj):
        self._send(code, json.dumps(obj, ensure_ascii=False).encode("utf-8"),
                   "application/json; charset=utf-8")

    def _authorized(self, qs) -> bool:
        got = self.headers.get(TOKEN_HEADER) or (qs.get("t") or [""])[0]
        return hmac.compare_digest(str(got), self.server.app.token)

    def _body(self):
        try:
            n = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            return None
        if n <= 0 or n > MAX_BODY:
            return None
        try:
            return json.loads(self.rfile.read(n).decode("utf-8"))
        except (ValueError, UnicodeDecodeError):
            return None

    # ---------- GET ----------

    def do_GET(self):
        u = urlparse(self.path)
        qs = parse_qs(u.query)
        if not self._authorized(qs):
            return self._json(403, {"error": "令牌无效"})
        p = u.path
        app = self.server.app

        if p in ("/", "/index.html"):
            try:
                return self._send(200, INDEX.read_bytes(), "text/html; charset=utf-8")
            except OSError:
                return self._json(500, {"error": "index.html 不存在"})

        if p == "/api/state":
            doc, variables, _ = guardian.load_rules()
            deny = [guardian.expand(t, variables)
                    for t in guardian.DENY_SUBTREE_TEMPLATES + guardian.DENY_EXACT_TEMPLATES]
            return self._json(200, {
                "app": "disk-guardian",
                "version": doc.get("version"),
                "updated": doc.get("updated"),
                "note": doc.get("note", ""),
                "levels": doc.get("levels", {}),
                "c": drive_info("C"),
                "d": drive_info("D"),
                "admin": guardian.is_admin(),
                "deny_ok": guardian.deny_selftest(variables),
                "deny_paths": deny,
                "rule_count": len(doc.get("rules", [])),
                "server_time": datetime.now().isoformat(timespec="seconds"),
            })

        if p == "/api/scan":
            try:
                level = int((qs.get("level") or ["1"])[0])
            except ValueError:
                level = 1
            level = max(1, min(3, level))
            refresh = (qs.get("refresh") or ["0"])[0] == "1"
            rows = app.plan(level, refresh)
            ready = sum(x["size_bytes"] for x in rows if x["status"] == "ready")
            return self._json(200, {
                "generated_at": datetime.now().isoformat(timespec="seconds"),
                "level": level,
                "ready_bytes": ready,
                "ready": guardian.human(ready),
                "rules": rows,
            })

        if p == "/api/recycle":
            roots = ["C:\\"]
            items = recycle.list_items(roots)
            total = sum(i[0] for i in items)
            rows = [{"size": s, "size_h": guardian.human(s), "path": orig}
                    for s, orig, _, _ in sorted(items, reverse=True)[:200]]
            return self._json(200, {
                "drives": roots, "count": len(items),
                "bytes": total, "size": guardian.human(total), "items": rows,
            })

        if p == "/api/logs":
            guardian.OUT.mkdir(parents=True, exist_ok=True)
            files = sorted(guardian.OUT.glob("*.log"),
                           key=lambda f: f.stat().st_mtime, reverse=True)[:12]
            newest = files[0] if files else None
            tail = []
            if newest:
                try:
                    tail = newest.read_text(encoding="utf-8", errors="replace").splitlines()[-120:]
                except OSError:
                    pass
            actions = []
            if guardian.ACTION_LOG.exists():
                try:
                    actions = guardian.ACTION_LOG.read_text(
                        encoding="utf-8", errors="replace").splitlines()[-40:]
                except OSError:
                    pass
            return self._json(200, {
                "files": [{"name": f.name, "size": f.stat().st_size} for f in files],
                "tail": tail, "actions": actions,
            })

        return self._json(404, {"error": "not found"})

    # ---------- POST ----------

    def do_POST(self):
        u = urlparse(self.path)
        qs = parse_qs(u.query)
        if not self._authorized(qs):
            return self._json(403, {"error": "令牌无效"})
        body = self._body()
        if not isinstance(body, dict):
            return self._json(400, {"error": "请求体不是 JSON 对象"})
        app = self.server.app
        p = u.path

        if p == "/api/apply":
            try:
                level = int(body.get("level", 1))
            except (ValueError, TypeError):
                return self._json(400, {"error": "level 非法"})
            if level not in (1, 2, 3):
                return self._json(400, {"error": "level 只能是 1/2/3"})
            ids = body.get("ids", [])
            if (not isinstance(ids, list) or len(ids) > 100
                    or not all(isinstance(i, str) for i in ids)):
                return self._json(400, {"error": "ids 非法"})
            if body.get("confirm") != guardian.expected_token(level):
                app.logger("GUI apply 被拒：确认令牌不正确")
                return self._json(403, {"error": "确认令牌不正确，未删除任何东西"})
            # A FRESH plan: never execute against a 30s-old scan.
            rows = app.plan(level, refresh=True)
            result = guardian.execute_rules(rows, ids, level, app.logger,
                                            preconfirmed=True)
            app.invalidate()
            return self._json(200 if "error" not in result else 409, result)

        if p == "/api/purge":
            if body.get("confirm") != "PURGE":
                app.logger("GUI purge 被拒：确认令牌不正确")
                return self._json(403, {"error": "确认令牌不正确，回收站未动"})
            drives = body.get("drives") or ["C"]
            if not isinstance(drives, list) or len(drives) > 8:
                return self._json(400, {"error": "drives 非法"})
            roots = []
            for d in drives:
                d = str(d).strip().rstrip(":\\").upper()
                if len(d) != 1 or not d.isalpha():
                    return self._json(400, {"error": "drives 非法"})
                roots.append(d + ":\\")
            items = recycle.list_items(roots)
            app.logger("GUI purge: %s, %d 项, %s" %
                       (",".join(roots), len(items),
                        guardian.human(sum(i[0] for i in items))))
            ok, failed = recycle.empty_safely(roots)
            guardian.log_action({"action": "purge", "source": "gui",
                                 "drives": roots, "ok": ok,
                                 "failed": [{"path": p2, "rc": rc} for p2, rc in failed]})
            return self._json(200, {
                "ok": ok, "failed": len(failed),
                "c_free": round(guardian.free_gib("C:\\")[0], 2),
            })

        if p == "/api/quit":
            app.logger("收到 /api/quit，准备关闭界面服务")
            threading.Thread(target=self.server.shutdown, daemon=True).start()
            return self._json(200, {"ok": True})

        return self._json(404, {"error": "not found"})


# ---------- address / window ----------

def normalize_addr(addr: str):
    if not addr:
        return "127.0.0.1", 0
    host, _, port_s = addr.rpartition(":")
    host = host.strip("[]")
    if host == "localhost":
        return host, int(port_s or 0)
    try:
        ip = ipaddress.ip_address(host)
    except ValueError:
        raise SystemExit("只允许监听回环地址，收到 %r" % addr)
    if not ip.is_loopback:
        raise SystemExit("只允许监听回环地址，收到 %r" % addr)
    return host, int(port_s or 0)


BROWSER_CANDIDATES = [
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    "msedge",
    "chrome",
]


def open_app_window(url: str):
    """Chromium --app= gives a chromeless window with its own taskbar entry."""
    tail = ["--app=" + url, "--window-size=1320,900"]
    for exe in BROWSER_CANDIDATES:
        if os.path.isabs(exe):
            if not os.path.isfile(exe):
                continue
        elif shutil.which(exe) is None:
            continue
        try:
            subprocess.Popen([exe] + tail, stdout=subprocess.DEVNULL,
                             stderr=subprocess.DEVNULL, close_fds=True)
            return exe
        except OSError:
            continue
    try:
        subprocess.Popen(["rundll32.exe", "url.dll,FileProtocolHandler", url],
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                         close_fds=True)
        return "rundll32"
    except OSError:
        return None


def main(argv=None):
    ap = argparse.ArgumentParser(description="disk-guardian GUI server")
    ap.add_argument("--no-open", action="store_true",
                    help="do not launch the browser window (verification)")
    ap.add_argument("--addr", default="",
                    help="loopback address:port, e.g. 127.0.0.1:8899")
    args = ap.parse_args(argv)

    host, port = normalize_addr(args.addr)
    token = secrets.token_hex(16)
    app = App(token)
    httpd = ThreadingHTTPServer((host, port), Handler)
    httpd.daemon_threads = True
    httpd.app = app
    actual_addr = "%s:%d" % httpd.server_address[:2]
    url = "http://%s/?t=%s" % (actual_addr, token)

    guardian.OUT.mkdir(parents=True, exist_ok=True)
    RUN_MARK.write_text(json.dumps({
        "addr": actual_addr, "url": url, "token": token,
        "pid": os.getpid(), "started": datetime.now().isoformat(timespec="seconds"),
    }, ensure_ascii=False, indent=2), encoding="utf-8")

    app.logger("=== disk-guardian GUI start | %s ===" % actual_addr)
    if not args.no_open:
        opened = open_app_window(url)
        app.logger("window opened via: %s" % (opened or "FAILED"))
    else:
        app.logger("--no-open: 未自动打开窗口（验证模式）")

    try:
        httpd.serve_forever(poll_interval=0.4)
    except KeyboardInterrupt:
        pass
    finally:
        try:
            RUN_MARK.unlink(missing_ok=True)
        except OSError:
            pass
        app.logger("=== disk-guardian GUI stopped ===")
        app.logger.close()
    return 0


if __name__ == "__main__":
    # pythonw.exe has no console: a startup failure must leave a file behind.
    try:
        sys.exit(main())
    except SystemExit:
        raise
    except Exception:
        import traceback
        try:
            guardian.OUT.mkdir(parents=True, exist_ok=True)
            (guardian.OUT / "gui_crash.txt").write_text(
                traceback.format_exc(), encoding="utf-8")
        except OSError:
            pass
        sys.exit(1)
