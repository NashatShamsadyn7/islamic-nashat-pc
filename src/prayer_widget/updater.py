"""Checks GitHub Releases for a newer build.

The app is downloaded directly rather than from a store, so nothing tells a user that a new
version exists. The release the CI workflow publishes on every push is the source of truth.
"""

from __future__ import annotations

import json
import urllib.error
import urllib.request
from dataclasses import dataclass

REPO = "NashatShamsadyn7/islamic-nashat-desktop"
RELEASES_API = f"https://api.github.com/repos/{REPO}/releases/latest"
RELEASES_PAGE = f"https://github.com/{REPO}/releases/latest"

_TIMEOUT_SECONDS = 8


@dataclass(frozen=True)
class Release:
    version: tuple[int, ...]
    tag: str
    url: str
    notes: str


def parse_version(text: str) -> tuple[int, ...]:
    """``v1.2.3`` and ``1.2.3`` both become ``(1, 2, 3)``.

    Anything unparseable becomes ``(0,)``, which never beats a real version - a malformed
    tag must not produce an update prompt that leads nowhere.
    """
    cleaned = text.strip().lstrip("vV")
    parts: list[int] = []
    for chunk in cleaned.split("."):
        digits = ""
        for char in chunk:
            if not char.isdigit():
                break
            digits += char
        if not digits:
            break
        parts.append(int(digits))
    return tuple(parts) if parts else (0,)


def is_newer(candidate: str, current: str) -> bool:
    return parse_version(candidate) > parse_version(current)


def fetch_latest() -> Release | None:
    """Returns the newest published release, or None for any failure.

    No connectivity, a rate limit or a repository with no releases yet must all be silent:
    the user did not ask for this check and cannot act on its failure.
    """
    request = urllib.request.Request(
        RELEASES_API,
        headers={
            "Accept": "application/vnd.github+json",
            "User-Agent": "islamic-nashat-desktop",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=_TIMEOUT_SECONDS) as response:
            if response.status != 200:
                return None
            payload = json.loads(response.read().decode("utf-8"))
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError, OSError):
        return None

    tag = payload.get("tag_name") or ""
    if not tag:
        return None

    return Release(
        version=parse_version(tag),
        tag=tag,
        url=payload.get("html_url") or RELEASES_PAGE,
        notes=(payload.get("body") or "").strip(),
    )


def check(current_version: str) -> Release | None:
    """The newest release if it beats ``current_version``, otherwise None."""
    latest = fetch_latest()
    if latest is None:
        return None
    return latest if latest.version > parse_version(current_version) else None
