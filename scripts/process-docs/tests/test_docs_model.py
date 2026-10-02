from docs_model import MODULE_HEADINGS, REQUIRED_HEADINGS, parse_doc, validate_catalog

BODY = "\n\n".join(f"{h}\n\nNone." for h in REQUIRED_HEADINGS)


def make(front: str, body: str = BODY) -> str:
    return f"---\n{front}\n---\n\n# Title\n\n{body}\n"


GOOD_FRONT = """process: calc-margins
kind: calculation
module: catalog
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


MODULE_BODY = "\n\n".join(f"{h}\n\nNone." for h in MODULE_HEADINGS)
MODULE_FRONT = """process: module-catalog
kind: module
module: catalog
summary: Product master data.
owns: []
verified_at: "1d75813bb"
related: []"""


def test_valid_module_doc_parses_with_empty_owns():
    doc, errors = parse_doc("docs/processes/module-catalog.md", make(MODULE_FRONT, MODULE_BODY))
    assert errors == []
    assert doc.kind == "module"
    assert doc.module == "catalog"


def test_module_doc_requires_module_headings():
    _, errors = parse_doc("docs/processes/module-catalog.md", make(MODULE_FRONT))
    assert any("## Users & screens" in e for e in errors)


def test_module_doc_name_must_match_module():
    front = MODULE_FRONT.replace("module: catalog", "module: bank")
    _, errors = parse_doc("docs/processes/module-catalog.md", make(front, MODULE_BODY))
    assert any("module-bank.md" in e for e in errors)


def test_missing_module_is_error():
    front = GOOD_FRONT.replace("module: catalog\n", "")
    _, errors = parse_doc("docs/processes/calc-margins.md", make(front))
    assert any("'module'" in e for e in errors)


def test_module_must_be_kebab_case():
    front = GOOD_FRONT.replace("module: catalog", "module: Catalog")
    _, errors = parse_doc("docs/processes/calc-margins.md", make(front))
    assert any("kebab-case" in e for e in errors)


def test_new_kinds_use_their_prefixes():
    for kind, stem in (("job", "job-photobank-index"), ("workflow", "flow-packing")):
        front = GOOD_FRONT.replace("process: calc-margins", f"process: {stem}").replace("kind: calculation", f"kind: {kind}")
        _, errors = parse_doc(f"docs/processes/{stem}.md", make(front))
        assert errors == [], errors
