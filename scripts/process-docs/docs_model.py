"""Process doc model: parse frontmatter + body and validate against the template."""
import re
from dataclasses import dataclass
from pathlib import Path, PurePosixPath

import yaml

DOCS_DIR = "docs/processes"
INDEX_NAME = "INDEX.md"
KIND_PREFIX = {"sync": "sync", "calculation": "calc", "feed": "feed"}
REQUIRED_HEADINGS = (
    "## Purpose",
    "## Trigger",
    "## Data flow",
    "## Logic & formulas",
    "## Configuration",
    "## Runtime facts",
    "## Known quirks",
    "## Code entry points",
)
REQUIRED_KEYS = ("process", "kind", "summary", "owns", "verified_at", "related")
SHA_RE = re.compile(r"\A[0-9a-f]{7,40}\Z")
FRONTMATTER_RE = re.compile(r"\A---\n(.*?)\n---\n(.*)\Z", re.S)


@dataclass(frozen=True)
class ProcessDoc:
    path: str
    process: str
    kind: str
    summary: str
    owns: tuple[str, ...]
    verified_at: str
    related: tuple[str, ...]
    body: str


def is_process_doc_name(name: str) -> bool:
    return name.endswith(".md") and name != INDEX_NAME and not name.startswith("_")


def _string_list(meta: dict, key: str, errors: list[str], prefix: str) -> tuple[str, ...]:
    value = meta.get(key)
    if not isinstance(value, list) or not all(isinstance(v, str) for v in value):
        errors.append(f"{prefix}: '{key}' must be a list of strings")
        return ()
    return tuple(value)


def _check_headings(body: str, errors: list[str], prefix: str) -> None:
    lines = [line.rstrip() for line in body.splitlines()]
    positions = []
    for heading in REQUIRED_HEADINGS:
        if heading not in lines:
            errors.append(f"{prefix}: missing heading '{heading}'")
        else:
            positions.append(lines.index(heading))
    if positions != sorted(positions):
        errors.append(f"{prefix}: headings are not in template order")


def _check_identity(meta: dict, stem: str, errors: list[str], prefix: str) -> None:
    kind = meta.get("kind")
    if kind not in KIND_PREFIX:
        errors.append(f"{prefix}: kind '{kind}' must be one of {sorted(KIND_PREFIX)}")
    elif not stem.startswith(KIND_PREFIX[kind] + "-"):
        errors.append(f"{prefix}: filename prefix must be '{KIND_PREFIX[kind]}-' for kind '{kind}'")
    if meta.get("process") != stem:
        errors.append(f"{prefix}: process '{meta.get('process')}' must equal filename stem '{stem}'")


def _check_verified_at(meta: dict, errors: list[str], prefix: str) -> str:
    value = meta.get("verified_at")
    if not isinstance(value, str):
        errors.append(f"{prefix}: verified_at must be a quoted string (quote the SHA, got {value!r})")
        return ""
    if not SHA_RE.match(value):
        errors.append(f"{prefix}: verified_at '{value}' is not a 7-40 char hex SHA")
    return value


def parse_doc(rel_path: str, text: str) -> tuple[ProcessDoc | None, list[str]]:
    prefix = rel_path
    match = FRONTMATTER_RE.match(text)
    if not match:
        return None, [f"{prefix}: missing or malformed frontmatter"]
    try:
        meta = yaml.safe_load(match.group(1)) or {}
    except yaml.YAMLError as exc:
        return None, [f"{prefix}: frontmatter is not valid YAML: {exc}"]
    if not isinstance(meta, dict):
        return None, [f"{prefix}: frontmatter must be a mapping"]

    errors = [f"{prefix}: missing key '{k}'" for k in REQUIRED_KEYS if k not in meta]
    stem = PurePosixPath(rel_path).stem
    _check_identity(meta, stem, errors, prefix)
    verified_at = _check_verified_at(meta, errors, prefix)
    owns = _string_list(meta, "owns", errors, prefix)
    if not owns:
        errors.append(f"{prefix}: 'owns' must list at least one glob")
    related = _string_list(meta, "related", errors, prefix)
    summary = meta.get("summary")
    if not isinstance(summary, str) or not summary.strip():
        errors.append(f"{prefix}: 'summary' must be a non-empty string")
    body = match.group(2)
    _check_headings(body, errors, prefix)

    if errors:
        return None, errors
    return ProcessDoc(rel_path, stem, meta["kind"], summary.strip(), owns, verified_at, related, body), []


def validate_catalog(docs: list[ProcessDoc]) -> list[str]:
    names = {d.process for d in docs}
    return [
        f"{d.path}: related '{r}' is not a known process"
        for d in docs
        for r in d.related
        if r not in names
    ]


def load_docs(repo: Path) -> tuple[list[ProcessDoc], list[str]]:
    docs_dir = repo / DOCS_DIR
    if not docs_dir.is_dir():
        return [], []
    docs: list[ProcessDoc] = []
    errors: list[str] = []
    for file in sorted(docs_dir.iterdir()):
        if not is_process_doc_name(file.name):
            continue
        doc, doc_errors = parse_doc(f"{DOCS_DIR}/{file.name}", file.read_text(encoding="utf-8"))
        errors.extend(doc_errors)
        if doc:
            docs.append(doc)
    return docs, errors + validate_catalog(docs)
