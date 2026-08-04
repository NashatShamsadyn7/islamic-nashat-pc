"""Start-with-Windows, via the Startup folder rather than the registry.

A registry Run entry is what antivirus heuristics flag first, and an unsigned downloaded
exe is already suspicious enough. The Startup folder is plain, visible, and the user can
undo it by deleting one file.

A one-line VBScript is used instead of a .cmd because cmd flashes a console window on every
login, which looks broken.
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

ENTRY_NAME = "IslamicNashatWidget.vbs"


def startup_dir() -> Path:
    appdata = os.environ.get("APPDATA")
    if not appdata:
        raise OSError("APPDATA is not set")
    return Path(appdata) / "Microsoft" / "Windows" / "Start Menu" / "Programs" / "Startup"


def _entry_path() -> Path:
    return startup_dir() / ENTRY_NAME


def _launch_target() -> str:
    """The command to run at login.

    When frozen this is the exe itself; when running from source it is the interpreter and
    the package, so the feature can be exercised during development too.
    """
    if getattr(sys, "frozen", False):
        return f'"{Path(sys.executable)}"'
    return f'"{Path(sys.executable)}" -m prayer_widget'


def is_enabled() -> bool:
    try:
        return _entry_path().exists()
    except OSError:
        return False


def set_enabled(enabled: bool) -> bool:
    """Returns whether the requested state was actually reached."""
    try:
        path = _entry_path()
        if not enabled:
            path.unlink(missing_ok=True)
            return True

        path.parent.mkdir(parents=True, exist_ok=True)
        # VBScript escapes a double quote by doubling it. The command already contains
        # quotes around the path, so they have to survive into the string literal - a path
        # under "Program Files" would otherwise be split at the space.
        literal = _launch_target().replace('"', '""')
        # 0 as the second argument hides the window; False means do not wait for it.
        path.write_text(
            f'CreateObject("WScript.Shell").Run "{literal}", 0, False\n',
            encoding="utf-8",
        )
        return True
    except OSError:
        return False
