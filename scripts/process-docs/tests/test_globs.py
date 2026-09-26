from globs import glob_to_regex, matches_any


def test_double_star_slash_matches_zero_or_more_dirs():
    rx = glob_to_regex("backend/src/**/Flexi/**")
    assert rx.match("backend/src/Flexi/A.cs")
    assert rx.match("backend/src/x/y/Flexi/sub/A.cs")
    assert not rx.match("backend/test/Flexi/A.cs")


def test_single_star_does_not_cross_directories():
    rx = glob_to_regex("backend/src/*.cs")
    assert rx.match("backend/src/A.cs")
    assert not rx.match("backend/src/sub/A.cs")


def test_star_inside_segment():
    assert glob_to_regex("backend/**/FlexiLedger*.cs").match("backend/a/FlexiLedgerClient.cs")


def test_special_chars_are_escaped():
    assert glob_to_regex("a/b.c").match("a/b.c")
    assert not glob_to_regex("a/b.c").match("a/bxc")


def test_matches_any():
    assert matches_any("x/y.cs", ["nope/**", "x/*.cs"])
    assert not matches_any("x/y.cs", [])
