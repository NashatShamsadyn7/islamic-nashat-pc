import pytest

from prayer_widget.updater import is_newer, parse_version


class TestParseVersion:
    @pytest.mark.parametrize(
        "text, expected",
        [
            ("v1.2.3", (1, 2, 3)),
            ("1.2.3", (1, 2, 3)),
            ("V0.1.0", (0, 1, 0)),
            ("2.0", (2, 0)),
            ("1.0.4-beta", (1, 0, 4)),
        ],
    )
    def test_parses(self, text: str, expected: tuple[int, ...]) -> None:
        assert parse_version(text) == expected

    @pytest.mark.parametrize("text", ["", "nightly", "v", "release"])
    def test_an_unparseable_tag_loses_to_everything(self, text: str) -> None:
        # A malformed tag must never win, or the app would prompt for an "update" that
        # leads to a release page with nothing newer on it.
        assert parse_version(text) == (0,)
        assert not is_newer(text, "0.1.0")


class TestIsNewer:
    def test_a_higher_patch_wins(self) -> None:
        assert is_newer("v1.0.4", "1.0.3")

    def test_the_same_version_is_not_newer(self) -> None:
        assert not is_newer("v1.0.3", "1.0.3")

    def test_an_older_version_is_not_newer(self) -> None:
        assert not is_newer("v1.0.2", "1.0.3")

    def test_numbers_compare_numerically_not_as_text(self) -> None:
        # The bug that hides until the tenth release: "10" sorts before "9" as a string.
        assert is_newer("v1.0.10", "1.0.9")
        assert is_newer("v1.10.0", "1.9.0")

    def test_a_leading_v_is_ignored_on_both_sides(self) -> None:
        assert is_newer("v2.0.0", "v1.9.9")

    def test_a_shorter_version_is_treated_as_lower(self) -> None:
        assert is_newer("1.0.1", "1.0")
