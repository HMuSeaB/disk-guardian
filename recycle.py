"""Windows Recycle Bin wrapper with no modal-dialog deadlocks.

Why this module exists
----------------------
`SHEmptyRecycleBinW` shows a blocking error dialog when one item is broken
(observed 2026-11-07: `$R02DUPK` -> 0x80004005). Even with
SHERB_NOCONFIRMATION / NOPROGRESSUI that dialog still appears and hangs the
caller. So we never use it. Instead we enumerate the `$I` / `$R` pairs and
delete them in batches with SHFileOperationW (no FOF_ALLOWUNDO), skipping
whatever fails. No UI, no hang, idempotent.
"""
import os
import ctypes

FO_DELETE = 3
FOF_SILENT = 0x0004
FOF_NOCONFIRMATION = 0x0010
FOF_ALLOWUNDO = 0x0040
FOF_NOERRORUI = 0x0400
FOF_NOCONFIRMMKDIR = 0x0200

# Permanent delete flags: deliberately WITHOUT FOF_ALLOWUNDO.
FLAGS_PERMANENT = FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI
# Send-to-trash flags.
FLAGS_TRASH = FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_ALLOWUNDO


class SHFILEOPSTRUCTW(ctypes.Structure):
    _fields_ = [
        ("hwnd", ctypes.c_void_p),
        ("wFunc", ctypes.c_uint),
        ("pFrom", ctypes.c_void_p),
        ("pTo", ctypes.c_void_p),
        ("fFlags", ctypes.c_uint16),
        ("fAnyOperationsAborted", ctypes.c_int),
        ("hNameMappings", ctypes.c_void_p),
        ("lpszProgressTitle", ctypes.c_void_p),
    ]


_shell32 = ctypes.WinDLL("shell32", use_last_error=True)
_shell32.SHFileOperationW.argtypes = [ctypes.POINTER(SHFILEOPSTRUCTW)]
_shell32.SHFileOperationW.restype = ctypes.c_int


def _delete(paths, flags):
    """paths: list of absolute paths. Returns (rc, aborted)."""
    if not paths:
        return 0, False
    multi = "\0".join(paths) + "\0"
    buf = ctypes.create_unicode_buffer(multi)
    op = SHFILEOPSTRUCTW()
    op.hwnd = None
    op.wFunc = FO_DELETE
    op.pFrom = ctypes.addressof(buf)
    op.pTo = None
    op.fFlags = flags
    op.fAnyOperationsAborted = 0
    op.hNameMappings = None
    op.lpszProgressTitle = None
    rc = _shell32.SHFileOperationW(ctypes.byref(op))
    return rc, bool(op.fAnyOperationsAborted)


def send_to_recycle_bin(path):
    """Move one directory/file to the Recycle Bin. Returns (rc, still_exists)."""
    rc, _ = _delete([os.path.abspath(path)], FLAGS_TRASH)
    return rc, os.path.exists(path)


def send_many(paths, batch=40, permanent=False):
    """Delete many paths without ever showing a modal dialog.

    Reversible by default (FOF_ALLOWUNDO). On a failing batch we retry each
    item individually, so one locked/broken entry cannot block the rest.
    Returns (ok_list, failed_list) where failed_list holds (path, rc).
    """
    flags = FLAGS_PERMANENT if permanent else FLAGS_TRASH
    targets = [os.path.abspath(p) for p in paths]
    ok, failed = [], []
    for i in range(0, len(targets), batch):
        chunk = targets[i:i + batch]
        rc, _ = _delete(chunk, flags)
        if rc == 0:
            ok.extend(chunk)
            continue
        for p in chunk:
            rc1, _ = _delete([p], flags)
            if rc1 == 0:
                ok.append(p)
            else:
                failed.append((p, rc1))
    return ok, failed


def _recycle_dirs(drives=None):
    if drives is None:
        drives = ["C:\\"]
    for drive in drives:
        base = os.path.join(drive, "$Recycle.Bin")
        if os.path.isdir(base):
            yield base


def list_items(drives=None):
    """Read `$I` metadata. Returns list of (size, original_path, sid_dir, meta_file)."""
    rows = []
    for base in _recycle_dirs(drives):
        try:
            sids = os.listdir(base)
        except OSError:
            continue
        for sid in sids:
            d = os.path.join(base, sid)
            if not os.path.isdir(d):
                continue
            try:
                names = os.listdir(d)
            except OSError:
                continue
            for name in names:
                if not name.startswith("$I"):
                    continue
                meta = os.path.join(d, name)
                try:
                    with open(meta, "rb") as fh:
                        raw = fh.read()
                    version = int.from_bytes(raw[0:8], "little")
                    size = int.from_bytes(raw[8:16], "little")
                    if version >= 2:
                        plen = int.from_bytes(raw[24:28], "little")
                        original = raw[28:28 + plen * 2].decode("utf-16-le", errors="ignore")
                    else:
                        original = raw[24:24 + 520].decode("utf-16-le", errors="ignore")
                    rows.append((size, original.rstrip("\0"), d, meta))
                except (OSError, ValueError, IndexError):
                    continue
    return rows


def empty_safely(drives=None, batch=40):
    """Empty the Recycle Bin without SHEmptyRecycleBinW.

    Deletes `$I`/`$R` pairs in batches; on a failing batch, retries each item
    individually so one broken entry cannot block everything else.
    Returns (deleted_count, failed_list).
    """
    targets = []
    for base in _recycle_dirs(drives):
        try:
            sids = os.listdir(base)
        except OSError:
            continue
        for sid in sids:
            d = os.path.join(base, sid)
            if not os.path.isdir(d):
                continue
            try:
                for name in os.listdir(d):
                    if name.startswith("$I") or name.startswith("$R"):
                        targets.append(os.path.join(d, name))
            except OSError:
                pass

    failed = []
    ok = 0
    for i in range(0, len(targets), batch):
        chunk = targets[i:i + batch]
        rc, _ = _delete(chunk, FLAGS_PERMANENT)
        if rc == 0:
            ok += len(chunk)
            continue
        for p in chunk:
            rc1, _ = _delete([p], FLAGS_PERMANENT)
            if rc1 == 0:
                ok += 1
            else:
                failed.append((p, rc1))
    return ok, failed


if __name__ == "__main__":
    import sys
    items = list_items()
    print("recycle bin items:", len(items))
    for size, original, _, _ in sorted(items, reverse=True)[:15]:
        print("  %10.1f MiB  %s" % (size / 2 ** 20, original))
    if "--list-only" not in sys.argv:
        ok, failed = empty_safely()
        print("deleted:", ok, "failed:", len(failed))
        for p, rc in failed[:10]:
            print("  FAILED rc=%s %s" % (rc, p))
