from docs_model import REQUIRED_HEADINGS, parse_doc, validate_catalog

BODY = "\n\n".join(f"{h}\n\nNone." for h in REQUIRED_HEADINGS)


def make(front: str, body: str = BODY) -> str:
    return f"---\n{front}\n---\n\n# Title\n\n{body}\n"


GOOD_FRONT = """process: calc-margins
kind: calculation
summary: Computes M0-M3 margins.
owns:
  - backend/src/**/Margins/**
verified_at: "1d75813bb"
related: []"""


def test_valid_doc_parses():
    doc, errors = parse_doc("docs/processes/calc-margins.md", make(GOOD_FRONT))
    assert errors == []
    assert doc.process == "calc-margins"
    assert doc.owns == ("backend/src/**/Margins/**",)
    assert doc.related == ()


def test_missing_frontmatter_is_error():
    doc, errors = parse_doc("docs/processes/calc-x.md", "# no frontmatter")
    assert doc is None
    assert any("frontmatter" in e for e in errors)


def test_process_must_equal_filename_stem():
    _, errors = parse_doc("docs/processes/calc-other.md", make(GOOD_FRONT))
    assert any("filename" in e for e in errors)


def test_prefix_must_match_kind():
    front = GOOD_FRONT.replace("process: calc-margins", "process: sync-margins")
    _, errors = parse_doc("docs/processes/sync-margins.md", make(front))
    assert any("prefix" in e for e in errors)


def test_unknown_kind_is_error():
    front = GOOD_FRONT.replace("kind: calculation", "kind: report")
    _, errors = parse_doc("docs/processes/calc-margins.md", make(front))
    assert any("kind" in e for e in errors)


def test_unquoted_numeric_verified_at_is_error():
    front = GOOD_FRONT.replace('verified_at: "1d75813bb"', "verified_at: 1234567")
    _, errors = parse_doc("docs/processes/calc-margins.md", make(front))
    assert any("verified_at" in e and "quote" in e for e in errors)


def test_leading_zero_sha_parsed_as_octal_is_error():
    front = GOOD_FRONT.replace('verified_at: "1d75813bb"', "verified_at: 01234567")
    _, errors = parse_doc("docs/processes/calc-margins.md", make(front))
    assert any("verified_at" in e for e in errors)


def test_empty_owns_is_error():
    front = GOOD_FRONT.replace("owns:\n  - backend/src/**/Margins/**", "owns: []")
    _, errors = parse_doc("docs/processes/calc-margins.md", make(front))
    assert any("owns" in e for e in errors)


def test_missing_heading_is_error():
    body = BODY.replace("## Known quirks", "## Quirks")
    _, errors = parse_doc("docs/processes/calc-margins.md", make(GOOD_FRONT, body))
    assert any("## Known quirks" in e for e in errors)


def test_headings_out_of_order_is_error():
    body = "\n\n".join(f"{h}\n\nNone." for h in reversed(REQUIRED_HEADINGS))
    _, errors = parse_doc("docs/processes/calc-margins.md", make(GOOD_FRONT, body))
    assert any("order" in e for e in errors)


def test_catalog_rejects_unknown_related():
    doc, _ = parse_doc("docs/processes/calc-margins.md",
                       make(GOOD_FRONT.replace("related: []", "related: [feed-nope]")))
    errors = validate_catalog([doc])
    assert any("feed-nope" in e for e in errors)
