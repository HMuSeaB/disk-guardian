#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Generate the double-click .bat launchers for disk-guardian.

Do NOT hand-edit the generated .bat files. They must stay:
  * GB18030 encoded, CRLF line endings, no BOM
  * with a `chcp 936` line
  * with ASCII-only labels / goto targets
  * with filenames containing NO half-width parentheses -- a `)` in the
    filename closes an `if (...)` block early when `%~f0` is expanded inside
    it, and the window just flashes and exits with no error at all.
  * using `goto :LABEL` for branching instead of `if (...) (...)` blocks

Usage:
    python tools/make_launchers.py              # write both default locations
    python tools/make_launchers.py --verify     # only check existing files
    python tools/make_launchers.py --out DIR    # write to one explicit dir
"""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PY = r"C:\Users\36230\.workbuddy\binaries\python\versions\3.13.12\python.exe"
PYW = r"C:\Users\36230\.workbuddy\binaries\python\versions\3.13.12\pythonw.exe"
APP = r"D:\4rchive\Code\disk-guardian\guardian.py"
SRV = r"D:\4rchive\Code\disk-guardian\ui\server.py"
OUT_DIR = r"D:\4rchive\Code\disk-guardian\out"

DEFAULT_TARGETS = [
    ROOT / "launchers",
    Path(r"C:\Users\36230\SystemTools\disk-guardian"),
]

NOAPP = [
    ":NOAPP",
    "echo 找不到主程序：%APP%",
    "echo 请确认项目还在 D:\\4rchive\\Code\\disk-guardian\\",
    "pause",
    "exit /b 1",
]

NOSRV = [
    ":NOSRV",
    "echo 找不到界面服务：%SRV%",
    "echo 请确认项目还在 D:\\4rchive\\Code\\disk-guardian\\",
    "pause",
    "exit /b 1",
]

HEAD = [
    "@echo off",
    "chcp 936 >nul",
    "setlocal",
    "REM 托管 Python 开了 UTF-8 模式：固定住，避免控制台 936 与管道输出不一致",
    'set "PYTHONUTF8=1"',
    'set "PYTHONIOENCODING=utf-8"',
]

BATS = {
    "1-预览可清理.bat": HEAD + [
        "title disk-guardian 预览",
        'set "PY=__PY__"',
        'set "APP=__APP__"',
        'if not exist "%PY%" set "PY=python"',
        'if not exist "%APP%" goto :NOAPP',
        '"%PY%" "%APP%" --json-report',
        'set "RC=%errorlevel%"',
        "echo.",
        "echo 以上是只读预览，没有删除任何东西。",
        "echo 报告在 __OUT__\\last_report.json",
        "echo.",
        "pause",
        "exit /b %RC%",
    ] + NOAPP,

    "2-执行安全清理.bat": HEAD + [
        "title disk-guardian 执行安全清理",
        'set "PY=__PY__"',
        'set "APP=__APP__"',
        'if not exist "%PY%" set "PY=python"',
        'if not exist "%APP%" goto :NOAPP',
        "echo.",
        "echo 只处理 level 1 纯缓存。先列清单，再要求输入 DELETE 才会动手。",
        "echo 动作是移入回收站，可以撤销，不会永久删除。",
        "echo 要真正腾出空间，请另跑 3-清空回收站.bat。",
        "echo.",
        '"%PY%" "%APP%" --apply --level 1 --json-report',
        'set "RC=%errorlevel%"',
        "echo.",
        "pause",
        "exit /b %RC%",
    ] + NOAPP,

    "3-清空回收站.bat": HEAD + [
        "title disk-guardian 清空回收站",
        'set "PY=__PY__"',
        'set "APP=__APP__"',
        'if not exist "%PY%" set "PY=python"',
        'if not exist "%APP%" goto :NOAPP',
        "echo.",
        "echo 这一步是永久删除，不可撤销。",
        "echo 会先列出回收站清单，再要求输入 PURGE 确认。",
        "echo.",
        '"%PY%" "%APP%" --purge --json-report',
        'set "RC=%errorlevel%"',
        "echo.",
        "pause",
        "exit /b %RC%",
    ] + NOAPP,

    "4-打开报告目录.bat": HEAD + [
        "title disk-guardian 打开报告目录",
        'set "OUT=__OUT__"',
        'if not exist "%OUT%" goto :NOOUT',
        'explorer "%OUT%"',
        "exit /b 0",
        ":NOOUT",
        "echo 还没有输出目录，先跑 1-预览可清理.bat",
        "pause",
        "exit /b 1",
    ],

    "5-打开图形界面.bat": HEAD + [
        "title disk-guardian 图形界面",
        'set "PYW=__PYW__"',
        'set "SRV=__SRV__"',
        'if not exist "%PYW%" set "PYW=pythonw"',
        'if not exist "%SRV%" goto :NOSRV',
        "echo 正在启动 disk-guardian 图形界面，浏览器窗口稍后自动出现。",
        "echo 关闭请在界面右上角点「退出」；不要直接关服务进程。",
        'start "" "%PYW%" "%SRV%"',
        "timeout /t 2 >nul",
        "exit /b 0",
    ] + NOSRV,
}


def encode(lines):
    text = "\r\n".join(lines) + "\r\n"
    data = text.encode("gb18030")
    assert not data.startswith(b"\xef\xbb\xbf"), "BOM must not be present"
    assert data.startswith(b"@echo off\r\n"), "CRLF check failed"
    assert b"\n" not in data.replace(b"\r\n", b""), "bare LF found"
    # Round-trip: every character must survive a GB18030 decode.
    assert data.decode("gb18030") == text
    return data


def check(path: Path, expected: bytes, label: str):
    assert "(" not in path.name and ")" not in path.name, \
        "half-width parentheses in .bat filename will break cmd: %s" % path.name
    if not path.exists():
        print("MISSING %-10s %s" % (label, path))
        return False
    raw = path.read_bytes()
    ok = raw == expected
    print("%-7s %-10s %5d bytes  %s" % ("OK" if ok else "DIFF", label, len(raw), path))
    if not ok:
        print("        expected %d bytes, BOM=%s, bareLF=%s" %
              (len(expected), raw.startswith(b"\xef\xbb\xbf"),
               b"\n" in raw.replace(b"\r\n", b"")))
    return ok


def main(argv):
    verify = "--verify" in argv
    targets = DEFAULT_TARGETS
    if "--out" in argv:
        i = argv.index("--out")
        if i + 1 >= len(argv):
            raise SystemExit("--out needs a directory")
        targets = [Path(argv[i + 1])]

    all_ok = True
    for target in targets:
        if not verify:
            target.mkdir(parents=True, exist_ok=True)
        for name, lines in BATS.items():
            rendered = [ln.replace("__PY__", PY)
                          .replace("__PYW__", PYW)
                          .replace("__APP__", APP)
                          .replace("__SRV__", SRV)
                          .replace("__OUT__", OUT_DIR) for ln in lines]
            expected = encode(rendered)
            assert "if (" not in "\n".join(rendered), \
                "no if (...) blocks allowed: %s" % name
            path = target / name
            if not verify:
                path.write_bytes(expected)
            all_ok &= check(path, expected, "verify" if verify else "write")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
