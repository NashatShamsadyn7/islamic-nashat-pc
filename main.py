"""Frozen-build entry point.

Deliberately separate from ``prayer_widget/__main__.py``: PyInstaller analyses the script
it is handed as a top-level module, so the relative imports inside the package are invisible
to it and PySide6 never makes it into the bundle. Absolute imports from here give it a
chain it can follow.
"""

import sys

from prayer_widget.app import main

if __name__ == "__main__":
    sys.exit(main())
