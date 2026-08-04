"""Pins the Python calculator to what the Android app's Kotlin class actually returns.

``calculator_golden.json`` was produced by running ``MuslimDataCalculator`` itself - see
``CalculatorGoldenDumpTest`` in the Android repository. Reading the two implementations and
judging them equivalent is not the same as proving it; this compares strings.

Any difference at all fails, down to the second, because the two apps show the same user
the same prayer in the same city and a one-minute disagreement is indistinguishable from a
bug.
"""

from __future__ import annotations

import json
from datetime import date
from pathlib import Path

import pytest

from prayer_widget.calculator import PrayerCalculator, method_for_country

GOLDEN = json.loads(
    (Path(__file__).parent / "calculator_golden.json").read_text(encoding="utf-8")
)

KEYS = ("Fajr", "Sunrise", "Dhuhr", "Asr", "Sunset", "Maghrib", "Isha")

CASES = [
    (label, day)
    for label, case in GOLDEN.items()
    for day in case["days"]
]


def test_the_golden_dump_covers_every_calculation_method() -> None:
    methods = {case["method"] for case in GOLDEN.values()}
    from prayer_widget.calculator import METHOD_PARAMS

    assert methods == set(METHOD_PARAMS), "a method is untested and could be silently wrong"


@pytest.mark.parametrize("label, day_text", CASES)
def test_matches_the_android_calculator_exactly(label: str, day_text: str) -> None:
    case = GOLDEN[label]
    calculator = PrayerCalculator(
        method=case["method"],
        asr_juristic=case["asrJuristic"],
        adjust_high_lats=3,
    )

    year, month, day = (int(part) for part in day_text.split("-"))
    produced = calculator.prayer_times(
        date(year, month, day), case["lat"], case["lng"], case["tz"]
    )

    expected = case["days"][day_text]
    for key in KEYS:
        assert produced[key] == expected[key], (
            f"{label} {day_text} {key}: python={produced[key]} kotlin={expected[key]}"
        )


class TestMethodForCountry:
    @pytest.mark.parametrize(
        "country, method",
        [
            ("SA", "MAKKAH"),
            ("KW", "MAKKAH"),
            ("EG", "EGYPT"),
            ("PK", "KARACHI"),
            ("IR", "TEHRAN"),
            ("US", "ISNA"),
            ("CA", "ISNA"),
        ],
    )
    def test_known_countries(self, country: str, method: str) -> None:
        assert method_for_country(country) == method

    def test_iraq_and_anything_unknown_fall_back_to_mwl(self) -> None:
        # Matches PrayerManager.autoMethodForCountry: Iraq is not special-cased there.
        assert method_for_country("IQ") == "MWL"
        assert method_for_country("ZZ") == "MWL"

    def test_case_and_emptiness_are_tolerated(self) -> None:
        assert method_for_country("sa") == "MAKKAH"
        assert method_for_country("") == "MWL"
