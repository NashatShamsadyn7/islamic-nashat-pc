# PyInstaller spec: one windowed exe, no console, assets embedded.
# Build with:  pyinstaller build.spec --noconfirm

block_cipher = None

analysis = Analysis(
    # main.py, not the package's __main__.py: PyInstaller analyses the given script as a
    # top-level module, so relative imports inside the package are invisible to it and
    # PySide6 silently never enters the bundle.
    ["main.py"],
    pathex=["src"],
    binaries=[],
    # Loaded through config.bundled_path(), which resolves sys._MEIPASS when frozen.
    datas=[
        # 166,404 places and the 45,749 fixed timetable rows, lifted straight out of the
        # Android app's muslim-data database so both agree on every city.
        ("assets/locations.db", "assets"),
        ("assets/azan_makkah.mp3", "assets"),
    ],
    hiddenimports=[],
    hookspath=[],
    runtime_hooks=[],
    # Only the large modules this app plainly never touches. Qt widgets pull in more than
    # is obvious - QtOpenGL and QtNetwork among them - so the list stays conservative
    # rather than shaving megabytes at the risk of a runtime import failure that would
    # only appear on a machine without Qt installed.
    excludes=[
        "PySide6.QtWebEngineCore",
        "PySide6.QtWebEngineWidgets",
        "PySide6.QtQuick",
        "PySide6.QtQml",
        "PySide6.Qt3DCore",
        "PySide6.QtCharts",
        "PySide6.QtDataVisualization",
        "PySide6.QtDesigner",
        "tkinter",
    ],
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(analysis.pure, analysis.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    analysis.scripts,
    analysis.binaries,
    analysis.zipfiles,
    analysis.datas,
    [],
    name="IslamicNashatWidget",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    runtime_tmpdir=None,
    # No console: this is a tray app, and a black window at every launch looks broken.
    console=False,
    disable_windowed_traceback=False,
)
