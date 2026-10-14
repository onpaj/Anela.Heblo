# Process Docs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the infrastructure for an agent-facing process-docs catalog: the `docs/processes/` format with three exemplar docs, a staleness/orphan checker wired into PR CI, and two Heblo MCP tools that serve the docs to claude.ai.

**Architecture:** Markdown docs with YAML frontmatter live in `docs/processes/`. A Python script (`scripts/process-docs/`) validates them, finds stale/orphan/dead-glob problems against git, and generates `INDEX.md`. The same markdown files are embedded into `Anela.Heblo.Application` as resources, parsed at startup by `EmbeddedProcessDocStore`, exposed via two MediatR use cases and a thin `ProcessDocsMcpTools` class gated by a new `Anela_ProcessDocs` permission.

**Tech Stack:** Python 3 + PyYAML + pytest (script); .NET 8, MediatR, YamlDotNet, FuzzySharp, xUnit + Moq (backend); GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-24-process-docs-design.md`

## Global Constraints

- Docs live in `docs/processes/`; one file per process; filename stem == `process` frontmatter value.
- Filename prefix per kind: `sync` → `sync-`, `calculation` → `calc-`, `feed` → `feed-`.
- Frontmatter keys, all required: `process`, `kind`, `summary`, `owns` (non-empty list), `verified_at` (quoted string, 7–40 hex chars), `related` (list, may be empty).
- Body headings, all required, in this order: `## Purpose`, `## Trigger`, `## Data flow`, `## Logic & formulas`, `## Configuration`, `## Runtime facts`, `## Known quirks`, `## Code entry points`.
- `INDEX.md` is generated, never hand-edited. Files starting with `_` (the template) and `INDEX.md` are not process docs.
- Docs are written in English.
- DTOs are classes, never records (CLAUDE.md). Every `*Response` inherits `BaseResponse`.
- Orphan check runs in **warn** mode in this PR.
- Backend gate: `dotnet build` + `dotnet format --verify-no-changes`. Use `-p:UseSharedCompilation=false` and `--no-build` for test runs if another worktree is building concurrently.

## Review Focus

1. **`verified_at` that YAML parses as a number** (e.g. `1234567`, or `01234567` which PyYAML reads as octal, unquoted) — expect a clear schema error, not a crash or a silently wrong SHA. Pinned in Task 1.
2. **`verified_at` not in history** (squash-merged branch, typo) — expect the doc reported stale with reason `unknown-commit`, not a git exception. Pinned in Task 2.
3. **Jobs implemented through an abstract base** (`BankImportJobBase`, `DailyInvoiceImportJobBase`) — expect concrete subclasses detected as jobs and the abstract base not reported. Pinned in Task 2.
4. **A malformed embedded doc at runtime** — expect that doc skipped and logged, the other docs still served. Pinned in Task 5.
5. **Unknown / misspelled process name from claude.ai** (`GetProcessDoc("margins")`) — expect an MCP error listing close matches (`calc-margins`). Pinned in Task 6.

---

## File Structure

```
docs/processes/
  _TEMPLATE.md                     # template, not served
  INDEX.md                         # generated
  calc-margins.md                  # exemplar
  sync-flexi-analytics.md          # exemplar
  calc-stock-up.md                 # exemplar
scripts/process-docs/
  config.yaml                      # orphan_mode, include, ignore
  conftest.py                      # puts this dir on sys.path for pytest
  globs.py                         # glob -> regex, matching
  docs_model.py                    # ProcessDoc, parse + validate
  git_ops.py                       # thin git wrappers
  analysis.py                      # stale, dead globs, orphans, PR check
  index_gen.py                     # INDEX.md rendering
  check.py                         # CLI: check | index | pr
  tests/
    helpers.py                     # temp git repo builder
    test_globs.py
    test_docs_model.py
    test_analysis.py
    test_index_gen.py
    test_cli.py
backend/src/Anela.Heblo.Application/Features/ProcessDocs/
  ProcessDoc.cs                    # internal model
  ProcessDocParser.cs              # frontmatter + body parsing
  IProcessDocStore.cs
  EmbeddedProcessDocStore.cs
  ProcessDocsModule.cs
  Contracts/ProcessDocDtos.cs
  UseCases/ListProcesses/ListProcessesRequest.cs   (+ Response)
  UseCases/ListProcesses/ListProcessesHandler.cs
  UseCases/GetProcessDoc/GetProcessDocRequest.cs   (+ Response)
  UseCases/GetProcessDoc/GetProcessDocHandler.cs
backend/src/Anela.Heblo.API/MCP/Tools/ProcessDocsMcpTools.cs
backend/test/Anela.Heblo.Tests/Features/ProcessDocs/*.cs
backend/test/Anela.Heblo.Tests/MCP/Tools/ProcessDocsMcpToolsTests.cs
.claude/skills/process/SKILL.md
docs/routines/process-docs-refresh.md
```

Modified: `Anela.Heblo.Application.csproj`, `ApplicationModule.cs`, `McpModule.cs`, `Dockerfile`, `access-matrix.json` (+ generated files), `.github/workflows/ci-feature-branch.yml`, `CLAUDE.md`, `docs/integrations/mcp-server.md`.

---

### Task 1: Script foundation — globs and doc model

**Files:**
- Create: `scripts/process-docs/conftest.py`, `scripts/process-docs/globs.py`, `scripts/process-docs/docs_model.py`
- Test: `scripts/process-docs/tests/test_globs.py`, `scripts/process-docs/tests/test_docs_model.py`

**Interfaces:**
- Produces:
  - `globs.glob_to_regex(pattern: str) -> re.Pattern[str]`
  - `globs.matches_any(path: str, patterns: Iterable[str]) -> bool`
  - `docs_model.ProcessDoc` (frozen dataclass: `path: str, process: str, kind: str, summary: str, owns: tuple[str, ...], verified_at: str, related: tuple[str, ...], body: str`)
  - `docs_model.KIND_PREFIX: dict[str, str]`, `docs_model.REQUIRED_HEADINGS: tuple[str, ...]`, `docs_model.DOCS_DIR = "docs/processes"`
  - `docs_model.parse_doc(rel_path: str, text: str) -> tuple[ProcessDoc | None, list[str]]`
  - `docs_model.validate_catalog(docs: list[ProcessDoc]) -> list[str]`
  - `docs_model.load_docs(repo: Path) -> tuple[list[ProcessDoc], list[str]]`

- [ ] **Step 1: Install test deps locally**

Run: `python3 -m pip install --user pyyaml pytest`
Expected: installs (pytest is not present on this machine yet).

- [ ] **Step 2: Create `conftest.py`**

```python
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, str(Path(__file__).parent / "tests"))
```

- [ ] **Step 3: Write failing glob tests** — `tests/test_globs.py`

```python
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
```

- [ ] **Step 4: Run — expect FAIL**

Run: `python3 -m pytest scripts/process-docs/tests/test_globs.py -q`
Expected: FAIL, `ModuleNotFoundError: No module named 'globs'`.

- [ ] **Step 5: Implement `globs.py`**

```python
"""Gitignore-style glob matching against repo-relative POSIX paths."""
import re
from functools import lru_cache
from typing import Iterable


@lru_cache(maxsize=None)
def glob_to_regex(pattern: str) -> re.Pattern[str]:
    parts: list[str] = []
    i = 0
    while i < len(pattern):
        if pattern.startswith("**/", i):
            parts.append("(?:.*/)?")
            i += 3
        elif pattern.startswith("**", i):
            parts.append(".*")
            i += 2
        elif pattern[i] == "*":
            parts.append("[^/]*")
            i += 1
        elif pattern[i] == "?":
            parts.append("[^/]")
            i += 1
        else:
            parts.append(re.escape(pattern[i]))
            i += 1
    return re.compile("".join(parts) + r"\Z")


def matches_any(path: str, patterns: Iterable[str]) -> bool:
    return any(glob_to_regex(p).match(path) for p in patterns)
```

- [ ] **Step 6: Run — expect PASS**

Run: `python3 -m pytest scripts/process-docs/tests/test_globs.py -q`
Expected: 5 passed.

- [ ] **Step 7: Write failing doc-model tests** — `tests/test_docs_model.py`

```python
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
```

- [ ] **Step 8: Run — expect FAIL**

Run: `python3 -m pytest scripts/process-docs/tests/test_docs_model.py -q`
Expected: FAIL, `No module named 'docs_model'`.

- [ ] **Step 9: Implement `docs_model.py`**

```python
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
```

- [ ] **Step 10: Run — expect PASS**

Run: `python3 -m pytest scripts/process-docs/tests -q`
Expected: 16 passed.

- [ ] **Step 11: Commit**

```bash
git add scripts/process-docs
git commit -m "feat: add process-docs model and glob matching"
```

---

### Task 2: Script analysis — staleness, dead globs, orphans, PR check

**Files:**
- Create: `scripts/process-docs/git_ops.py`, `scripts/process-docs/analysis.py`, `scripts/process-docs/tests/helpers.py`
- Test: `scripts/process-docs/tests/test_analysis.py`

**Interfaces:**
- Consumes: `ProcessDoc`, `matches_any` (Task 1)
- Produces:
  - `git_ops.tracked_files(repo: Path) -> list[str]`, `git_ops.commit_exists(repo, sha) -> bool`, `git_ops.changed_files(repo, from_ref, to_ref) -> list[str]`, `git_ops.merge_base(repo, a, b) -> str`, `git_ops.commit_time(repo, sha) -> int`
  - `analysis.StaleDoc` (frozen dataclass: `process: str, reason: str, files: tuple[str, ...]`), reasons `"changed"` / `"unknown-commit"`
  - `analysis.find_stale(repo, docs, ref="HEAD") -> list[StaleDoc]`
  - `analysis.find_dead_globs(docs, files) -> list[str]`
  - `analysis.find_job_files(repo, files) -> list[str]`
  - `analysis.find_orphans(repo, docs, files, include, ignore) -> list[str]`
  - `analysis.find_untouched_in_pr(docs, changed) -> list[StaleDoc]`
  - `analysis.oldest_verified(repo, docs, limit) -> list[str]`
  - `tests/helpers.py`: `init_repo(path) -> Path`, `commit(repo, files: dict[str, str], message="c") -> str`

- [ ] **Step 1: Write the repo helper** — `tests/helpers.py`

```python
import os
import subprocess
from pathlib import Path

GIT = ["git", "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false"]


def run(repo: Path, *args: str) -> str:
    return subprocess.run([*GIT, *args], cwd=repo, check=True, capture_output=True, text=True).stdout.strip()


def init_repo(path: Path) -> Path:
    path.mkdir(parents=True, exist_ok=True)
    run(path, "init", "-q", "-b", "main")
    return path


def commit(repo: Path, files: dict[str, str], message: str = "c", date: str | None = None) -> str:
    """date: ISO timestamp for author+committer (tests that order by commit time need distinct dates)."""
    for rel, content in files.items():
        target = repo / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")
    run(repo, "add", "-A")
    env = {**os.environ, "GIT_AUTHOR_DATE": date, "GIT_COMMITTER_DATE": date} if date else None
    subprocess.run([*GIT, "commit", "-q", "-m", message], cwd=repo, check=True, capture_output=True, env=env)
    return run(repo, "rev-parse", "HEAD")
```

- [ ] **Step 2: Write failing analysis tests** — `tests/test_analysis.py`

```python
from analysis import (find_dead_globs, find_job_files, find_orphans, find_stale,
                      find_untouched_in_pr, oldest_verified)
from docs_model import ProcessDoc
from git_ops import tracked_files
from helpers import commit, init_repo


def doc(process="calc-a", owns=("src/a/**",), verified_at="0000000", path=None):
    return ProcessDoc(path or f"docs/processes/{process}.md", process, "calculation",
                      "s", tuple(owns), verified_at, (), "")


def test_doc_is_stale_when_owned_file_changed_since_verified(tmp_path):
    repo = init_repo(tmp_path)
    base = commit(repo, {"src/a/x.cs": "1", "src/b/y.cs": "1"})
    commit(repo, {"src/a/x.cs": "2"})
    stale = find_stale(repo, [doc(verified_at=base[:9])])
    assert [(s.process, s.reason, s.files) for s in stale] == [("calc-a", "changed", ("src/a/x.cs",))]


def test_doc_not_stale_when_only_unowned_files_changed(tmp_path):
    repo = init_repo(tmp_path)
    base = commit(repo, {"src/a/x.cs": "1", "src/b/y.cs": "1"})
    commit(repo, {"src/b/y.cs": "2"})
    assert find_stale(repo, [doc(verified_at=base)]) == []


def test_unknown_verified_at_is_reported_not_raised(tmp_path):
    repo = init_repo(tmp_path)
    commit(repo, {"src/a/x.cs": "1"})
    stale = find_stale(repo, [doc(verified_at="deadbeef")])
    assert stale[0].reason == "unknown-commit"


def test_dead_glob_reported():
    errors = find_dead_globs([doc(owns=("src/a/**", "src/gone/**"))], ["src/a/x.cs"])
    assert len(errors) == 1 and "src/gone/**" in errors[0]


JOB_DIRECT = "public sealed class SyncJob : IRecurringJob { }"
JOB_BASE = "public abstract class ImportJobBase : IRecurringJob { }"
JOB_DERIVED = "public sealed class CzkImportJob : ImportJobBase { }"
NOT_JOB = "public class Helper : IDisposable { }"


def test_job_detection_follows_abstract_bases_and_skips_them(tmp_path):
    repo = init_repo(tmp_path)
    commit(repo, {
        "backend/src/A/SyncJob.cs": JOB_DIRECT,
        "backend/src/B/ImportJobBase.cs": JOB_BASE,
        "backend/src/B/CzkImportJob.cs": JOB_DERIVED,
        "backend/src/C/Helper.cs": NOT_JOB,
    })
    jobs = find_job_files(repo, tracked_files(repo))
    assert jobs == ["backend/src/A/SyncJob.cs", "backend/src/B/CzkImportJob.cs"]


def test_orphans_exclude_owned_and_ignored_and_add_included(tmp_path):
    repo = init_repo(tmp_path)
    commit(repo, {
        "backend/src/A/SyncJob.cs": JOB_DIRECT,
        "backend/src/B/OtherJob.cs": JOB_DIRECT.replace("SyncJob", "OtherJob"),
        "backend/src/C/IgnoredJob.cs": JOB_DIRECT.replace("SyncJob", "IgnoredJob"),
        "backend/src/D/MarginCalc.cs": "public class MarginCalc {}",
    })
    files = tracked_files(repo)
    orphans = find_orphans(repo, [doc(owns=("backend/src/A/**",))], files,
                           include=["backend/src/D/*.cs"], ignore=["backend/src/C/**"])
    assert orphans == ["backend/src/B/OtherJob.cs", "backend/src/D/MarginCalc.cs"]


def test_pr_flags_doc_whose_code_changed_without_doc():
    docs = [doc(), doc(process="calc-b", owns=("src/b/**",))]
    flagged = find_untouched_in_pr(docs, ["src/a/x.cs", "src/b/y.cs", "docs/processes/calc-b.md"])
    assert [(f.process, f.files) for f in flagged] == [("calc-a", ("src/a/x.cs",))]


def test_oldest_verified_orders_by_commit_time_unknown_first(tmp_path):
    repo = init_repo(tmp_path)
    first = commit(repo, {"f": "1"}, date="2026-01-01T00:00:00")
    second = commit(repo, {"f": "2"}, date="2026-02-01T00:00:00")
    docs = [doc("calc-new", verified_at=second), doc("calc-old", verified_at=first),
            doc("calc-bad", verified_at="deadbeef")]
    assert oldest_verified(repo, docs, limit=2) == ["calc-bad", "calc-old"]
```

- [ ] **Step 3: Run — expect FAIL**

Run: `python3 -m pytest scripts/process-docs/tests/test_analysis.py -q`
Expected: FAIL, `No module named 'analysis'`.

- [ ] **Step 4: Implement `git_ops.py`**

```python
"""Thin wrappers over the git CLI."""
import subprocess
from pathlib import Path


def _git(repo: Path, *args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(["git", *args], cwd=repo, check=check, capture_output=True, text=True)


def tracked_files(repo: Path) -> list[str]:
    return _git(repo, "ls-files").stdout.split("\n")[:-1]


def commit_exists(repo: Path, sha: str) -> bool:
    return _git(repo, "cat-file", "-e", f"{sha}^{{commit}}", check=False).returncode == 0


def changed_files(repo: Path, from_ref: str, to_ref: str) -> list[str]:
    out = _git(repo, "diff", "--name-only", f"{from_ref}..{to_ref}").stdout
    return [line for line in out.split("\n") if line]


def merge_base(repo: Path, a: str, b: str) -> str:
    return _git(repo, "merge-base", a, b).stdout.strip()


def commit_time(repo: Path, sha: str) -> int:
    return int(_git(repo, "show", "-s", "--format=%ct", sha).stdout.strip())
```

- [ ] **Step 5: Implement `analysis.py`**

```python
"""Staleness, dead globs, orphan jobs and PR checks for process docs."""
import re
from dataclasses import dataclass
from pathlib import Path

from docs_model import ProcessDoc
from git_ops import changed_files, commit_exists, commit_time
from globs import glob_to_regex, matches_any

JOB_ROOT = "IRecurringJob"
CLASS_RE = re.compile(
    r"\b(?P<mods>(?:\w+\s+)*)class\s+(?P<name>\w+)(?:<[^>]*>)?\s*(?:\([^)]*\))?\s*:\s*(?P<bases>[^{\n]+)"
)


@dataclass(frozen=True)
class StaleDoc:
    process: str
    reason: str
    files: tuple[str, ...]


def find_stale(repo: Path, docs: list[ProcessDoc], ref: str = "HEAD") -> list[StaleDoc]:
    stale = []
    for doc in docs:
        if not commit_exists(repo, doc.verified_at):
            stale.append(StaleDoc(doc.process, "unknown-commit", ()))
            continue
        owned = tuple(f for f in changed_files(repo, doc.verified_at, ref) if matches_any(f, doc.owns))
        if owned:
            stale.append(StaleDoc(doc.process, "changed", owned))
    return stale


def find_dead_globs(docs: list[ProcessDoc], files: list[str]) -> list[str]:
    return [
        f"{doc.path}: owns glob '{pattern}' matches no tracked file"
        for doc in docs
        for pattern in doc.owns
        if not any(glob_to_regex(pattern).match(f) for f in files)
    ]


def _parse_classes(repo: Path, files: list[str]) -> list[tuple[str, str, bool, set[str]]]:
    classes = []
    for rel in files:
        if not (rel.startswith("backend/src/") and rel.endswith(".cs")):
            continue
        text = (repo / rel).read_text(encoding="utf-8", errors="ignore")
        for m in CLASS_RE.finditer(text):
            bases = {b.strip().split("<")[0].split(".")[-1] for b in m["bases"].split(",")}
            classes.append((rel, m["name"], "abstract" in m["mods"].split(), bases))
    return classes


def find_job_files(repo: Path, files: list[str]) -> list[str]:
    classes = _parse_classes(repo, files)
    job_types = {JOB_ROOT}
    grew = True
    while grew:
        grew = False
        for _, name, _, bases in classes:
            if name not in job_types and bases & job_types:
                job_types.add(name)
                grew = True
    return sorted({rel for rel, name, is_abstract, _ in classes if name in job_types and not is_abstract})


def find_orphans(repo: Path, docs: list[ProcessDoc], files: list[str],
                 include: list[str], ignore: list[str]) -> list[str]:
    candidates = set(find_job_files(repo, files)) | {f for f in files if matches_any(f, include)}
    owned = [p for doc in docs for p in doc.owns]
    return sorted(f for f in candidates if not matches_any(f, owned) and not matches_any(f, ignore))


def find_untouched_in_pr(docs: list[ProcessDoc], changed: list[str]) -> list[StaleDoc]:
    changed_set = set(changed)
    flagged = []
    for doc in docs:
        if doc.path in changed_set:
            continue
        owned = tuple(f for f in changed if matches_any(f, doc.owns))
        if owned:
            flagged.append(StaleDoc(doc.process, "changed", owned))
    return flagged


def oldest_verified(repo: Path, docs: list[ProcessDoc], limit: int) -> list[str]:
    def age_key(doc: ProcessDoc) -> int:
        return commit_time(repo, doc.verified_at) if commit_exists(repo, doc.verified_at) else 0
    return [d.process for d in sorted(docs, key=age_key)[:limit]]
```

- [ ] **Step 6: Run — expect PASS**

Run: `python3 -m pytest scripts/process-docs/tests -q`
Expected: 24 passed.

- [ ] **Step 7: Sanity-check job detection on the real repo**

Run:
```bash
cd scripts/process-docs && python3 -c "
from pathlib import Path; from analysis import find_job_files; from git_ops import tracked_files
r=Path('../..').resolve(); j=find_job_files(r, tracked_files(r)); print(len(j)); print('\n'.join(j))"
```
Expected: 34 files; must include `DailyInvoiceImportCzkJob.cs` and `ComgateCzkImportJob.cs`; must NOT include `DailyInvoiceImportJobBase.cs` or `BankImportJobBase.cs`.

- [ ] **Step 8: Commit**

```bash
git add scripts/process-docs
git commit -m "feat: detect stale process docs, dead globs and orphan jobs"
```

---

### Task 3: Script CLI, index generation, config

**Files:**
- Create: `scripts/process-docs/index_gen.py`, `scripts/process-docs/check.py`, `scripts/process-docs/config.yaml`
- Test: `scripts/process-docs/tests/test_index_gen.py`, `scripts/process-docs/tests/test_cli.py`

**Interfaces:**
- Consumes: everything from Tasks 1–2
- Produces:
  - `index_gen.render_index(docs: list[ProcessDoc]) -> str`
  - CLI: `python3 scripts/process-docs/check.py [--repo PATH] check [--json]` (exit 1 on errors, or on orphans when `orphan_mode: fail`), `... index` (writes `docs/processes/INDEX.md`), `... pr --base REF --comment-file PATH` (always exit 0; writes the file only when docs are flagged)
  - JSON shape for `check --json`: `{"errors": [str], "orphans": [str], "orphan_mode": "warn"|"fail", "stale": [{"process", "reason", "files"}], "oldest": [str]}`

- [ ] **Step 1: Write failing index test** — `tests/test_index_gen.py`

```python
from docs_model import ProcessDoc
from index_gen import render_index


def d(process, kind, summary, related=()):
    return ProcessDoc(f"docs/processes/{process}.md", process, kind, summary, ("x",), "abcdef1", related, "")


def test_index_groups_by_kind_in_fixed_order_and_sorts_names():
    text = render_index([
        d("feed-metabase", "feed", "Views for Metabase."),
        d("calc-margins", "calculation", "M0-M3 margins.", ("sync-flexi",)),
        d("sync-flexi", "sync", "Flexi ledger mirror."),
        d("calc-alpha", "calculation", "Alpha."),
    ])
    assert text.startswith("<!-- GENERATED by scripts/process-docs/check.py index")
    assert text.index("## Syncs") < text.index("## Calculations") < text.index("## Feeds")
    assert text.index("calc-alpha") < text.index("calc-margins")
    assert "- [calc-margins](calc-margins.md) — M0-M3 margins. Related: sync-flexi" in text


def test_empty_kind_section_is_omitted():
    assert "## Feeds" not in render_index([d("sync-a", "sync", "A.")])
```

- [ ] **Step 2: Run — expect FAIL**

Run: `python3 -m pytest scripts/process-docs/tests/test_index_gen.py -q`
Expected: FAIL, `No module named 'index_gen'`.

- [ ] **Step 3: Implement `index_gen.py`**

```python
"""Render docs/processes/INDEX.md from process doc frontmatter."""
from docs_model import ProcessDoc

SECTIONS = (("sync", "Syncs"), ("calculation", "Calculations"), ("feed", "Feeds"))
HEADER = (
    "<!-- GENERATED by scripts/process-docs/check.py index — do not edit by hand -->\n"
    "# Process Index\n\n"
    "Agent-facing catalog of Heblo syncs, calculations and data feeds. "
    "Open the linked doc for the full process.\n"
)


def _line(doc: ProcessDoc) -> str:
    related = f" Related: {', '.join(doc.related)}" if doc.related else ""
    return f"- [{doc.process}]({doc.process}.md) — {doc.summary}{related}"


def render_index(docs: list[ProcessDoc]) -> str:
    parts = [HEADER]
    for kind, title in SECTIONS:
        members = sorted((d for d in docs if d.kind == kind), key=lambda d: d.process)
        if members:
            parts.append(f"\n## {title}\n\n" + "\n".join(_line(d) for d in members) + "\n")
    return "".join(parts)
```

- [ ] **Step 4: Write `config.yaml`**

```yaml
# Process-docs checker configuration.
# orphan_mode: warn during backfill; switch to fail once every job has a doc.
orphan_mode: warn
# Extra process-bearing files (beyond IRecurringJob implementations) that must be owned by a doc.
include: []
# Files that are never orphans (infrastructure, not business processes).
ignore: []
```

- [ ] **Step 5: Write failing CLI tests** — `tests/test_cli.py`

```python
import json
import subprocess
import sys
from pathlib import Path

from docs_model import REQUIRED_HEADINGS
from helpers import commit, init_repo

SCRIPT = Path(__file__).resolve().parents[1] / "check.py"
BODY = "\n\n".join(f"{h}\n\nNone." for h in REQUIRED_HEADINGS)
CONFIG = "orphan_mode: {mode}\ninclude: []\nignore: []\n"


def doc_text(verified_at: str) -> str:
    return (f'---\nprocess: sync-a\nkind: sync\nsummary: A sync.\nowns:\n  - backend/src/A/**\n'
            f'verified_at: "{verified_at}"\nrelated: []\n---\n\n# A\n\n{BODY}\n')


def run(repo, *args):
    return subprocess.run([sys.executable, str(SCRIPT), "--repo", str(repo), *args],
                          capture_output=True, text=True)


def setup(tmp_path, mode="warn", with_orphan=False):
    repo = init_repo(tmp_path)
    files = {"backend/src/A/SyncJob.cs": "public sealed class SyncJob : IRecurringJob {}",
             "scripts/process-docs/config.yaml": CONFIG.format(mode=mode)}
    if with_orphan:
        files["backend/src/B/OtherJob.cs"] = "public sealed class OtherJob : IRecurringJob {}"
    sha = commit(repo, files)
    commit(repo, {"docs/processes/sync-a.md": doc_text(sha[:9])})
    assert run(repo, "index").returncode == 0  # INDEX.md lives in the working tree; check reads it from disk
    return repo, sha


def test_check_passes_on_clean_catalog(tmp_path):
    repo, _ = setup(tmp_path)
    result = run(repo, "check")
    assert result.returncode == 0, result.stdout + result.stderr


def test_check_fails_on_index_drift(tmp_path):
    repo, _ = setup(tmp_path)
    (repo / "docs/processes/INDEX.md").write_text("stale", encoding="utf-8")
    result = run(repo, "check")
    assert result.returncode == 1
    assert "INDEX.md" in result.stdout


def test_orphan_warns_in_warn_mode_and_fails_in_fail_mode(tmp_path):
    warn_repo, _ = setup(tmp_path / "w", mode="warn", with_orphan=True)
    assert run(warn_repo, "check").returncode == 0
    fail_repo, _ = setup(tmp_path / "f", mode="fail", with_orphan=True)
    result = run(fail_repo, "check")
    assert result.returncode == 1
    assert "backend/src/B/OtherJob.cs" in result.stdout


def test_check_json_reports_stale(tmp_path):
    repo, _ = setup(tmp_path)
    commit(repo, {"backend/src/A/SyncJob.cs": "public sealed class SyncJob : IRecurringJob { int x; }"})
    data = json.loads(run(repo, "check", "--json").stdout)
    assert data["stale"] == [{"process": "sync-a", "reason": "changed", "files": ["backend/src/A/SyncJob.cs"]}]
    assert data["oldest"] == ["sync-a"]
    assert data["orphan_mode"] == "warn"


def test_pr_writes_comment_only_when_flagged(tmp_path):
    repo, _ = setup(tmp_path)
    subprocess.run(["git", "branch", "base"], cwd=repo, check=True)
    comment = tmp_path / "comment.md"
    assert run(repo, "pr", "--base", "base", "--comment-file", str(comment)).returncode == 0
    assert not comment.exists()
    commit(repo, {"backend/src/A/SyncJob.cs": "changed"})
    assert run(repo, "pr", "--base", "base", "--comment-file", str(comment)).returncode == 0
    text = comment.read_text(encoding="utf-8")
    assert "sync-a" in text and "backend/src/A/SyncJob.cs" in text
```

- [ ] **Step 6: Run — expect FAIL**

Run: `python3 -m pytest scripts/process-docs/tests/test_cli.py -q`
Expected: FAIL (check.py missing → non-zero return codes / JSON decode error).

- [ ] **Step 7: Implement `check.py`**

```python
#!/usr/bin/env python3
"""Validate docs/processes/ and report stale docs, orphan jobs and index drift.

Usage:
  check.py [--repo PATH] check [--json]
  check.py [--repo PATH] index
  check.py [--repo PATH] pr --base REF --comment-file PATH
"""
import argparse
import json
import sys
from dataclasses import asdict
from pathlib import Path

import yaml

from analysis import (find_dead_globs, find_orphans, find_stale, find_untouched_in_pr,
                      oldest_verified)
from docs_model import DOCS_DIR, INDEX_NAME, load_docs
from git_ops import changed_files, merge_base, tracked_files
from index_gen import render_index

CONFIG_PATH = "scripts/process-docs/config.yaml"
OLDEST_LIMIT = 5


def load_config(repo: Path) -> dict:
    path = repo / CONFIG_PATH
    config = yaml.safe_load(path.read_text(encoding="utf-8")) if path.exists() else {}
    config = config or {}
    mode = config.get("orphan_mode", "warn")
    if mode not in ("warn", "fail"):
        raise SystemExit(f"{CONFIG_PATH}: orphan_mode must be 'warn' or 'fail', got '{mode}'")
    return {"orphan_mode": mode, "include": config.get("include") or [], "ignore": config.get("ignore") or []}


def index_path(repo: Path) -> Path:
    return repo / DOCS_DIR / INDEX_NAME


def cmd_index(repo: Path) -> int:
    docs, errors = load_docs(repo)
    if errors:
        print("\n".join(errors))
        return 1
    index_path(repo).parent.mkdir(parents=True, exist_ok=True)
    index_path(repo).write_text(render_index(docs), encoding="utf-8")
    print(f"wrote {DOCS_DIR}/{INDEX_NAME} ({len(docs)} processes)")
    return 0


def cmd_check(repo: Path, as_json: bool) -> int:
    config = load_config(repo)
    docs, errors = load_docs(repo)
    files = tracked_files(repo)
    errors = errors + find_dead_globs(docs, files)
    current = index_path(repo).read_text(encoding="utf-8") if index_path(repo).exists() else ""
    if current != render_index(docs):
        errors.append(f"{DOCS_DIR}/{INDEX_NAME} is out of date — run: python3 scripts/process-docs/check.py index")
    orphans = find_orphans(repo, docs, files, config["include"], config["ignore"])
    stale = find_stale(repo, docs)
    failed = bool(errors) or (bool(orphans) and config["orphan_mode"] == "fail")

    if as_json:
        print(json.dumps({"errors": errors, "orphans": orphans, "orphan_mode": config["orphan_mode"],
                          "stale": [asdict(s) for s in stale],
                          "oldest": oldest_verified(repo, docs, OLDEST_LIMIT)}, indent=2))
        return 1 if failed else 0

    for e in errors:
        print(f"ERROR {e}")
    for o in orphans:
        print(f"{'ERROR' if config['orphan_mode'] == 'fail' else 'WARN'} orphan (no process doc owns it): {o}")
    for s in stale:
        print(f"INFO stale: {s.process} ({s.reason}) {', '.join(s.files)}")
    print("FAILED" if failed else f"OK: {len(docs)} process docs")
    return 1 if failed else 0


def cmd_pr(repo: Path, base: str, comment_file: Path) -> int:
    docs, _ = load_docs(repo)
    changed = changed_files(repo, merge_base(repo, base, "HEAD"), "HEAD")
    flagged = find_untouched_in_pr(docs, changed)
    if not flagged:
        print("no process docs affected")
        return 0
    lines = ["### Process docs may be out of date", "",
             "This PR changes code owned by these process docs without touching them. "
             "Update the doc, or bump its `verified_at` if behaviour didn't change.", ""]
    for f in flagged:
        lines.append(f"- `{DOCS_DIR}/{f.process}.md` — {', '.join(f'`{p}`' for p in f.files)}")
    comment_file.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    sub = parser.add_subparsers(dest="command", required=True)
    check = sub.add_parser("check")
    check.add_argument("--json", action="store_true")
    sub.add_parser("index")
    pr = sub.add_parser("pr")
    pr.add_argument("--base", required=True)
    pr.add_argument("--comment-file", type=Path, required=True)
    args = parser.parse_args(argv)
    repo = args.repo.resolve()
    if args.command == "index":
        return cmd_index(repo)
    if args.command == "check":
        return cmd_check(repo, args.json)
    return cmd_pr(repo, args.base, args.comment_file)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
```

- [ ] **Step 8: Run all script tests — expect PASS**

Run: `python3 -m pytest scripts/process-docs -q`
Expected: 31 passed.

- [ ] **Step 9: Commit**

```bash
git add scripts/process-docs
git commit -m "feat: add process-docs CLI with check, index and pr modes"
```

---

### Task 4: Template and three exemplar docs

**Files:**
- Create: `docs/processes/_TEMPLATE.md`, `docs/processes/calc-margins.md`, `docs/processes/sync-flexi-analytics.md`, `docs/processes/calc-stock-up.md`, `docs/processes/INDEX.md` (generated)

**Interfaces:**
- Consumes: `check.py index|check` (Task 3)
- Produces: three valid docs named `calc-margins`, `sync-flexi-analytics`, `calc-stock-up` — Task 5's "every embedded doc parses" test and Task 6's tests rely on at least these three existing.

- [ ] **Step 1: Create `_TEMPLATE.md`**

````markdown
---
process: <prefix>-<name>          # must equal the filename stem; prefix: sync | calc | feed
kind: sync                        # sync | calculation | feed
summary: One sentence — what data this moves or derives, and for whom.
owns:                             # repo-relative globs of the code this doc describes
  - backend/src/**/Feature/**
verified_at: "0000000"            # QUOTED short SHA of the commit this doc was checked against
related: []                       # other process names (upstream or downstream)
---

# <Human title>

## Purpose
Which business question this answers. Who looks at the result, and where (page, report, MCP tool).

## Trigger
Hangfire job id and cron (Europe/Prague), manual trigger in Recurring Jobs, or on-demand (request path).

## Data flow
Source (system / endpoint / table) → numbered steps → target (table / cache / view).
Name real tables, endpoints and cache keys.

## Logic & formulas
Exact rules. State units, with/without VAT, time windows, rounding, what is excluded and why.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|

## Runtime facts
Facts not derivable from code. Each line: fact — source — date checked.
Write "None" if there are none.

## Known quirks
Gotchas, edge cases, historical incidents. One bullet each, cause + effect.

## Code entry points
- `path/to/File.cs` — what to read it for
````

- [ ] **Step 2: Write `calc-margins.md`**

Read, in this order: `docs/features/margin-calculation-system.md`, `docs/features/margins_v2/`, `docs/features/product-margin-summary.md`, `backend/src/Anela.Heblo.Domain/Features/Catalog/Services/IMarginCalculationService.cs` and its implementation, `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/*.cs`, `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`, and `DataSourceOptions` (grep `ManufactureCostHistoryDays`).

Also fold in these agent-memory facts under *Runtime facts* / *Known quirks* (read each file in `/Users/pajgrtondrej/.claude/projects/-Users-pajgrtondrej-Work-GitHub-Anela-Heblo/memory/`): `project_margin_level_rename_m1_m3.md`, `project_prod_cost_window_365_appsetting.md`, `gotcha_m2_pool_is_59pct_of_overhead.md`, `gotcha_margin_average_window_mismatch.md`, `gotcha_semiproducts_inflate_m1a_denominator.md`, `gotcha_set_is_a_manufactured_product.md`, `gotcha_premature_merge_poisons_catalog_cache.md`. Verify each against current code first — drop anything the code no longer supports.

Frontmatter: `kind: calculation`, `owns` = the cost provider folder, the margin calculation service implementation, the GetProductMargins use case folder; `related: [sync-flexi-analytics]` only if the margins read data that sync produces — check, otherwise `[]`. Set `verified_at` to `git rev-parse --short=9 HEAD` (quoted).

- [ ] **Step 3: Write `sync-flexi-analytics.md`**

Read: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/` (start at `FlexiAnalyticsSyncJob.cs`), `docs/architecture/metabase.md`, `docs/architecture/development_guidelines.md` (ADR-007). Fold in memory files `project_flexi_raw_reporting_schema.md`, `gotcha_flexi_sync_shared_context_poison.md`, `gotcha_flexi_changedsince_is_ordered.md`, `gotcha_flexi_ledger_dto_is_a_view.md`, `gotcha_flexi_contact_list_empty_types.md`, `gotcha_analytics_db_never_configured.md`, `gotcha_czech_class5_is_not_operating_cost.md` — verified against current code (recent commits #4299 and #4301 changed failure handling). `kind: sync`, `owns: [backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/**]` plus any persistence folder it writes through.

- [ ] **Step 4: Write `calc-stock-up.md`**

Read: `docs/features/stock-up-process.md`, `docs/features/stock-up-state-consolidation-change-spec.md`, the stock-up use cases under `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/` (grep `StockUp`), `LogisticsStockOperationAdapter.cs`. Fold in `gotcha_stock_available_includes_manufacture_warehouse.md` and `gotcha_stock_planned_disjoint_from_total.md` if they apply. Pick the kind from what the code actually does: if the doc centres on computing the stock-up quantity, keep `calc-stock-up.md` / `kind: calculation`; if it is primarily a push of stock into Shoptet, name it `feed-stock-up.md` / `kind: feed`. Later tasks' tests use synthetic docs, so either name works — only Task 8's PR checklist wording mentions it.

- [ ] **Step 5: Generate index and check**

Run:
```bash
python3 scripts/process-docs/check.py index
python3 scripts/process-docs/check.py check
```
Expected: `OK: 3 process docs`; `WARN orphan` lines for the ~32 jobs not yet documented (warn mode — does not fail). Fix any `ERROR` lines.

- [ ] **Step 6: Commit**

```bash
git add docs/processes
git commit -m "docs: add process doc template and exemplar docs"
```

---

### Task 5: Backend store — parse and embed docs

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`, `backend/src/Anela.Heblo.Application/ApplicationModule.cs`, `Dockerfile`
- Create: `backend/src/Anela.Heblo.Application/Features/ProcessDocs/ProcessDoc.cs`, `ProcessDocParser.cs`, `IProcessDocStore.cs`, `EmbeddedProcessDocStore.cs`, `ProcessDocsModule.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/ProcessDocs/ProcessDocParserTests.cs`, `EmbeddedProcessDocStoreTests.cs`

**Interfaces:**
- Consumes: embedded docs from Task 4
- Produces:
  - `ProcessDoc` (internal record — domain type, not a DTO): `string Name, string Kind, string Summary, IReadOnlyList<string> Owns, string VerifiedAt, IReadOnlyList<string> Related, string Markdown`
  - `ProcessDocParser.Parse(string name, string text) -> ProcessDoc` (throws `FormatException`)
  - `IProcessDocStore { IReadOnlyList<ProcessDoc> All { get; } ProcessDoc? Find(string name); }`
  - `EmbeddedProcessDocStore(ILogger<EmbeddedProcessDocStore> logger)` and internal ctor `EmbeddedProcessDocStore(IEnumerable<(string Name, string Text)> sources, ILogger<EmbeddedProcessDocStore> logger)`; property `IReadOnlyList<string> LoadErrors`
  - `ProcessDocsModule.AddProcessDocsModule(this IServiceCollection)`
  - Resource logical names: `ProcessDocs/<stem>.md`

- [ ] **Step 1: Add YamlDotNet and the embedded resources to the Application csproj**

In `Anela.Heblo.Application.csproj`, add to the package `ItemGroup` (after `FuzzySharp`):
```xml
    <PackageReference Include="YamlDotNet" Version="16.3.0" />
```
And a new `ItemGroup` before `</Project>`:
```xml
  <ItemGroup>
    <!-- Agent-facing process docs, served by ProcessDocsMcpTools. See docs/processes/. -->
    <EmbeddedResource Include="../../../docs/processes/*.md"
                      Exclude="../../../docs/processes/_*.md;../../../docs/processes/INDEX.md"
                      LogicalName="ProcessDocs/%(Filename)%(Extension)" />
  </ItemGroup>
```

- [ ] **Step 2: Copy docs into the Docker build stage**

In `Dockerfile`, directly after `COPY backend/ ./backend/`:
```dockerfile
# Process docs are embedded into Anela.Heblo.Application (see its csproj)
COPY docs/processes/ ./docs/processes/
```

- [ ] **Step 3: Write failing parser tests** — `ProcessDocParserTests.cs`

```csharp
using Anela.Heblo.Application.Features.ProcessDocs;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProcessDocs;

public class ProcessDocParserTests
{
    private const string Valid = """
        ---
        process: calc-margins
        kind: calculation
        summary: Computes M0-M3 margins.
        owns:
          - backend/src/**/Margins/**
        verified_at: "1d75813bb"
        related:
          - sync-flexi-analytics
        ---

        # Margins

        ## Purpose
        Text.
        """;

    [Fact]
    public void Parse_ValidDoc_MapsFrontmatterAndKeepsFullMarkdown()
    {
        var doc = ProcessDocParser.Parse("calc-margins", Valid);

        Assert.Equal("calc-margins", doc.Name);
        Assert.Equal("calculation", doc.Kind);
        Assert.Equal("Computes M0-M3 margins.", doc.Summary);
        Assert.Equal(["backend/src/**/Margins/**"], doc.Owns);
        Assert.Equal("1d75813bb", doc.VerifiedAt);
        Assert.Equal(["sync-flexi-analytics"], doc.Related);
        Assert.Contains("## Purpose", doc.Markdown);
        Assert.DoesNotContain("verified_at", doc.Markdown);
    }

    [Fact]
    public void Parse_NoFrontmatter_Throws()
    {
        Assert.Throws<FormatException>(() => ProcessDocParser.Parse("calc-x", "# just markdown"));
    }

    [Fact]
    public void Parse_NameMismatch_Throws()
    {
        Assert.Throws<FormatException>(() => ProcessDocParser.Parse("calc-other", Valid));
    }

    [Fact]
    public void Parse_MissingSummary_Throws()
    {
        Assert.Throws<FormatException>(() =>
            ProcessDocParser.Parse("calc-margins", Valid.Replace("summary: Computes M0-M3 margins.\n", "")));
    }
}
```

- [ ] **Step 4: Write failing store tests** — `EmbeddedProcessDocStoreTests.cs`

```csharp
using Anela.Heblo.Application.Features.ProcessDocs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProcessDocs;

public class EmbeddedProcessDocStoreTests
{
    private static string Doc(string name, string kind = "calculation") => $"""
        ---
        process: {name}
        kind: {kind}
        summary: Summary of {name}.
        owns: [x/**]
        verified_at: "abcdef1"
        related: []
        ---

        # {name}
        """;

    [Fact]
    public void RealEmbeddedDocs_AllParse()
    {
        var store = new EmbeddedProcessDocStore(NullLogger<EmbeddedProcessDocStore>.Instance);

        Assert.Empty(store.LoadErrors);
        Assert.True(store.All.Count >= 3, $"expected >= 3 embedded process docs, got {store.All.Count}");
    }

    [Fact]
    public void MalformedDoc_IsSkippedAndOthersStillServed()
    {
        var store = new EmbeddedProcessDocStore(
            [("calc-good", Doc("calc-good")), ("calc-bad", "no frontmatter")],
            NullLogger<EmbeddedProcessDocStore>.Instance);

        Assert.Single(store.All);
        Assert.Equal("calc-good", store.All[0].Name);
        Assert.Single(store.LoadErrors);
        Assert.Contains("calc-bad", store.LoadErrors[0]);
    }

    [Fact]
    public void Find_IsCaseInsensitive_AndReturnsNullWhenMissing()
    {
        var store = new EmbeddedProcessDocStore([("calc-good", Doc("calc-good"))],
            NullLogger<EmbeddedProcessDocStore>.Instance);

        Assert.NotNull(store.Find("CALC-GOOD"));
        Assert.Null(store.Find("calc-missing"));
    }

    [Fact]
    public void All_IsSortedByName()
    {
        var store = new EmbeddedProcessDocStore(
            [("sync-b", Doc("sync-b", "sync")), ("calc-a", Doc("calc-a"))],
            NullLogger<EmbeddedProcessDocStore>.Instance);

        Assert.Equal(["calc-a", "sync-b"], store.All.Select(d => d.Name));
    }
}
```

- [ ] **Step 5: Run — expect FAIL (compile error)**

Run: `dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false`
Expected: FAIL, `The type or namespace name 'ProcessDocs' does not exist`.

- [ ] **Step 6: Implement `ProcessDoc.cs`**

```csharp
namespace Anela.Heblo.Application.Features.ProcessDocs;

/// <summary>An agent-facing process doc from docs/processes/, parsed from its frontmatter.</summary>
public sealed record ProcessDoc(
    string Name,
    string Kind,
    string Summary,
    IReadOnlyList<string> Owns,
    string VerifiedAt,
    IReadOnlyList<string> Related,
    string Markdown);
```

- [ ] **Step 7: Implement `ProcessDocParser.cs`**

```csharp
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Anela.Heblo.Application.Features.ProcessDocs;

/// <summary>
/// Parses a process doc. Full schema validation lives in scripts/process-docs/check.py (CI);
/// this only enforces what the MCP tools depend on.
/// </summary>
public static class ProcessDocParser
{
    private static readonly Regex FrontmatterRegex =
        new(@"\A---\r?\n(?<yaml>.*?)\r?\n---\r?\n(?<body>.*)\z", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static ProcessDoc Parse(string name, string text)
    {
        var match = FrontmatterRegex.Match(text);
        if (!match.Success)
        {
            throw new FormatException($"Process doc '{name}' has no frontmatter.");
        }

        var front = Yaml.Deserialize<Frontmatter?>(match.Groups["yaml"].Value)
            ?? throw new FormatException($"Process doc '{name}' has empty frontmatter.");

        if (front.Process != name)
        {
            throw new FormatException($"Process doc '{name}' declares process '{front.Process}'.");
        }

        return new ProcessDoc(
            name,
            Require(front.Kind, "kind", name),
            Require(front.Summary, "summary", name),
            front.Owns ?? [],
            Require(front.VerifiedAt, "verified_at", name),
            front.Related ?? [],
            match.Groups["body"].Value.TrimStart());
    }

    private static string Require(string? value, string key, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new FormatException($"Process doc '{name}' is missing '{key}'.")
            : value.Trim();

    private sealed class Frontmatter
    {
        public string? Process { get; set; }
        public string? Kind { get; set; }
        public string? Summary { get; set; }
        public List<string>? Owns { get; set; }
        public string? VerifiedAt { get; set; }
        public List<string>? Related { get; set; }
    }
}
```

- [ ] **Step 8: Implement `IProcessDocStore.cs` and `EmbeddedProcessDocStore.cs`**

```csharp
namespace Anela.Heblo.Application.Features.ProcessDocs;

public interface IProcessDocStore
{
    IReadOnlyList<ProcessDoc> All { get; }

    ProcessDoc? Find(string name);
}
```

```csharp
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ProcessDocs;

/// <summary>Loads process docs embedded into this assembly (see the csproj EmbeddedResource glob).</summary>
public sealed class EmbeddedProcessDocStore : IProcessDocStore
{
    private const string ResourcePrefix = "ProcessDocs/";
    private const string ResourceSuffix = ".md";

    private readonly Dictionary<string, ProcessDoc> _byName;

    public EmbeddedProcessDocStore(ILogger<EmbeddedProcessDocStore> logger)
        : this(ReadEmbedded(), logger)
    {
    }

    internal EmbeddedProcessDocStore(IEnumerable<(string Name, string Text)> sources, ILogger<EmbeddedProcessDocStore> logger)
    {
        var docs = new List<ProcessDoc>();
        var errors = new List<string>();
        foreach (var (name, text) in sources)
        {
            try
            {
                docs.Add(ProcessDocParser.Parse(name, text));
            }
            catch (Exception ex) when (ex is FormatException or YamlDotNet.Core.YamlException)
            {
                logger.LogError(ex, "Skipping malformed process doc {ProcessDoc}", name);
                errors.Add($"{name}: {ex.Message}");
            }
        }

        All = docs.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
        LoadErrors = errors;
        _byName = All.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ProcessDoc> All { get; }

    public IReadOnlyList<string> LoadErrors { get; }

    public ProcessDoc? Find(string name) => _byName.GetValueOrDefault(name.Trim());

    private static IEnumerable<(string Name, string Text)> ReadEmbedded()
    {
        var assembly = typeof(EmbeddedProcessDocStore).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !resource.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var name = resource[ResourcePrefix.Length..^ResourceSuffix.Length];
            yield return (name, reader.ReadToEnd());
        }
    }
}
```

- [ ] **Step 9: Implement `ProcessDocsModule.cs` and register it**

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ProcessDocs;

public static class ProcessDocsModule
{
    public static IServiceCollection AddProcessDocsModule(this IServiceCollection services)
    {
        services.AddSingleton<IProcessDocStore, EmbeddedProcessDocStore>();

        // MediatR handlers are automatically registered by AddMediatR scan
        return services;
    }
}
```

In `ApplicationModule.cs`, after `services.AddKnowledgeBaseModule(configuration);`:
```csharp
        services.AddProcessDocsModule();
```
Add `using Anela.Heblo.Application.Features.ProcessDocs;` with the other feature usings.

- [ ] **Step 10: Run — expect PASS**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests --no-build --filter "FullyQualifiedName~Features.ProcessDocs"
```
Expected: 8 passed. If `RealEmbeddedDocs_AllParse` reports 0 docs, the csproj glob path is wrong — print `typeof(EmbeddedProcessDocStore).Assembly.GetManifestResourceNames()` to debug.

- [ ] **Step 11: Verify the Docker stage still restores**

Run: `docker build --target backend-build -t heblo-procdocs-check .`
Expected: succeeds. (`docker` is aliased to podman here; run `podman machine start` first if needed.) If the machine can't run it, say so in the PR description rather than skipping silently.

- [ ] **Step 12: Commit**

```bash
git add backend/src/Anela.Heblo.Application Dockerfile backend/test/Anela.Heblo.Tests/Features/ProcessDocs
git commit -m "feat: embed and parse process docs in the application"
```

---

### Task 6: Use cases, permission and MCP tools

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ProcessDocs/Contracts/ProcessDocDtos.cs`, `UseCases/ListProcesses/ListProcessesRequest.cs`, `UseCases/ListProcesses/ListProcessesHandler.cs`, `UseCases/GetProcessDoc/GetProcessDocRequest.cs`, `UseCases/GetProcessDoc/GetProcessDocHandler.cs`, `backend/src/Anela.Heblo.API/MCP/Tools/ProcessDocsMcpTools.cs`
- Modify: `access-matrix.json` (+ regenerated files), `backend/src/Anela.Heblo.API/MCP/McpModule.cs`, `docs/integrations/mcp-server.md`
- Test: `backend/test/Anela.Heblo.Tests/Features/ProcessDocs/ProcessDocsHandlersTests.cs`, `backend/test/Anela.Heblo.Tests/MCP/Tools/ProcessDocsMcpToolsTests.cs`

**Interfaces:**
- Consumes: `IProcessDocStore`, `ProcessDoc`, internal store ctor (Task 5)
- Produces:
  - `ProcessSummaryDto { string Name, Kind, Summary, VerifiedAt; List<string> Related }`, `ProcessDocDto : ProcessSummaryDto { string Markdown }`
  - `ListProcessesRequest { string? Kind } : IRequest<ListProcessesResponse>`; `ListProcessesResponse : BaseResponse { List<ProcessSummaryDto> Processes }`
  - `GetProcessDocRequest { string Name } : IRequest<GetProcessDocResponse>`; `GetProcessDocResponse : BaseResponse { ProcessDocDto? Doc; List<string> Suggestions }`
  - `Feature.Anela_ProcessDocs` (read only)
  - MCP tools `ListProcesses(string? kind)`, `GetProcessDoc(string name)`

- [ ] **Step 1: Add the permission**

In `access-matrix.json` `features`, after the `Anela_MindMaps` entry:
```json
    { "key": "Anela_ProcessDocs", "label": "Procesní dokumentace" },
```
In every entry of `seedGroups`, append the read role to `roles` (default from the spec: all users with MCP access). Use a script so none is missed:
```bash
python3 - <<'EOF'
import json
p = "access-matrix.json"
d = json.load(open(p, encoding="utf-8"))
for g in d["seedGroups"]:
    if "anela.process_docs.read" not in g["roles"]:
        g["roles"].append("anela.process_docs.read")
open(p, "w", encoding="utf-8").write(json.dumps(d, ensure_ascii=False, indent=2) + "\n")
EOF
git diff --stat access-matrix.json
```
If the diff rewrites formatting beyond the added lines (the file uses one-line feature entries), revert and add the role lines by hand instead — only the new lines may change.

Regenerate:
```bash
cd backend && dotnet run --project tools/Anela.Heblo.AccessMatrixGen && cd ..
grep -n "Anela_ProcessDocs" backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs
grep -rn "process_docs" backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs
```
Expected: the enum value exists and the generated role string is `anela.process_docs.read`. If the generator names it differently, use the generated name in the seed groups and regenerate.

- [ ] **Step 2: Write DTOs and requests**

`Contracts/ProcessDocDtos.cs`:
```csharp
namespace Anela.Heblo.Application.Features.ProcessDocs.Contracts;

public class ProcessSummaryDto
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string VerifiedAt { get; set; } = string.Empty;
    public List<string> Related { get; set; } = [];
}

public class ProcessDocDto : ProcessSummaryDto
{
    public string Markdown { get; set; } = string.Empty;
}
```

`UseCases/ListProcesses/ListProcessesRequest.cs`:
```csharp
using Anela.Heblo.Application.Features.ProcessDocs.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.ProcessDocs.UseCases.ListProcesses;

public class ListProcessesRequest : IRequest<ListProcessesResponse>
{
    /// <summary>Optional filter: sync, calculation or feed.</summary>
    public string? Kind { get; set; }
}

public class ListProcessesResponse : BaseResponse
{
    public List<ProcessSummaryDto> Processes { get; set; } = [];
}
```

`UseCases/GetProcessDoc/GetProcessDocRequest.cs`:
```csharp
using Anela.Heblo.Application.Features.ProcessDocs.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.ProcessDocs.UseCases.GetProcessDoc;

public class GetProcessDocRequest : IRequest<GetProcessDocResponse>
{
    public string Name { get; set; } = string.Empty;
}

public class GetProcessDocResponse : BaseResponse
{
    /// <summary>Null when no process has that name; see <see cref="Suggestions"/>.</summary>
    public ProcessDocDto? Doc { get; set; }

    public List<string> Suggestions { get; set; } = [];
}
```

- [ ] **Step 3: Write failing handler tests** — `ProcessDocsHandlersTests.cs`

```csharp
using Anela.Heblo.Application.Features.ProcessDocs;
using Anela.Heblo.Application.Features.ProcessDocs.UseCases.GetProcessDoc;
using Anela.Heblo.Application.Features.ProcessDocs.UseCases.ListProcesses;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProcessDocs;

public class ProcessDocsHandlersTests
{
    private static string Doc(string name, string kind) => $"""
        ---
        process: {name}
        kind: {kind}
        summary: Summary of {name}.
        owns: [x/**]
        verified_at: "abcdef1"
        related: []
        ---

        # {name}

        ## Purpose
        Body of {name}.
        """;

    private static readonly EmbeddedProcessDocStore Store = new(
        [("calc-margins", Doc("calc-margins", "calculation")),
         ("sync-flexi-analytics", Doc("sync-flexi-analytics", "sync")),
         ("calc-stock-up", Doc("calc-stock-up", "calculation"))],
        NullLogger<EmbeddedProcessDocStore>.Instance);

    [Fact]
    public async Task ListProcesses_NoFilter_ReturnsAllSummariesWithoutMarkdown()
    {
        var result = await new ListProcessesHandler(Store).Handle(new ListProcessesRequest(), default);

        Assert.Equal(["calc-margins", "calc-stock-up", "sync-flexi-analytics"], result.Processes.Select(p => p.Name));
        Assert.Equal("Summary of calc-margins.", result.Processes[0].Summary);
    }

    [Fact]
    public async Task ListProcesses_KindFilter_IsCaseInsensitive()
    {
        var result = await new ListProcessesHandler(Store).Handle(new ListProcessesRequest { Kind = "SYNC" }, default);

        Assert.Equal(["sync-flexi-analytics"], result.Processes.Select(p => p.Name));
    }

    [Fact]
    public async Task GetProcessDoc_Found_ReturnsMarkdown()
    {
        var result = await new GetProcessDocHandler(Store).Handle(new GetProcessDocRequest { Name = "calc-margins" }, default);

        Assert.NotNull(result.Doc);
        Assert.Contains("Body of calc-margins.", result.Doc!.Markdown);
        Assert.Equal("abcdef1", result.Doc.VerifiedAt);
    }

    [Fact]
    public async Task GetProcessDoc_Misspelled_ReturnsNullDocAndCloseMatches()
    {
        var result = await new GetProcessDocHandler(Store).Handle(new GetProcessDocRequest { Name = "margins" }, default);

        Assert.Null(result.Doc);
        Assert.Contains("calc-margins", result.Suggestions);
    }
}
```

- [ ] **Step 4: Run — expect FAIL (compile error: handlers missing)**

Run: `dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false`
Expected: FAIL, `ListProcessesHandler` not found.

- [ ] **Step 5: Implement the handlers**

`UseCases/ListProcesses/ListProcessesHandler.cs`:
```csharp
using Anela.Heblo.Application.Features.ProcessDocs.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.ProcessDocs.UseCases.ListProcesses;

public class ListProcessesHandler : IRequestHandler<ListProcessesRequest, ListProcessesResponse>
{
    private readonly IProcessDocStore _store;

    public ListProcessesHandler(IProcessDocStore store)
    {
        _store = store;
    }

    public Task<ListProcessesResponse> Handle(ListProcessesRequest request, CancellationToken cancellationToken)
    {
        var docs = string.IsNullOrWhiteSpace(request.Kind)
            ? _store.All
            : _store.All.Where(d => string.Equals(d.Kind, request.Kind.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        return Task.FromResult(new ListProcessesResponse { Processes = docs.Select(ToSummary).ToList() });
    }

    internal static ProcessSummaryDto ToSummary(ProcessDoc doc) => new()
    {
        Name = doc.Name,
        Kind = doc.Kind,
        Summary = doc.Summary,
        VerifiedAt = doc.VerifiedAt,
        Related = doc.Related.ToList(),
    };
}
```

`UseCases/GetProcessDoc/GetProcessDocHandler.cs`:
```csharp
using Anela.Heblo.Application.Features.ProcessDocs.Contracts;
using FuzzySharp;
using MediatR;

namespace Anela.Heblo.Application.Features.ProcessDocs.UseCases.GetProcessDoc;

public class GetProcessDocHandler : IRequestHandler<GetProcessDocRequest, GetProcessDocResponse>
{
    private const int MaxSuggestions = 3;
    private const int MinSuggestionScore = 60;

    private readonly IProcessDocStore _store;

    public GetProcessDocHandler(IProcessDocStore store)
    {
        _store = store;
    }

    public Task<GetProcessDocResponse> Handle(GetProcessDocRequest request, CancellationToken cancellationToken)
    {
        var doc = _store.Find(request.Name);
        if (doc is not null)
        {
            return Task.FromResult(new GetProcessDocResponse { Doc = ToDto(doc) });
        }

        var suggestions = Process.ExtractTop(request.Name, _store.All.Select(d => d.Name), limit: MaxSuggestions)
            .Where(r => r.Score >= MinSuggestionScore)
            .Select(r => r.Value)
            .ToList();

        return Task.FromResult(new GetProcessDocResponse { Suggestions = suggestions });
    }

    private static ProcessDocDto ToDto(ProcessDoc doc) => new()
    {
        Name = doc.Name,
        Kind = doc.Kind,
        Summary = doc.Summary,
        VerifiedAt = doc.VerifiedAt,
        Related = doc.Related.ToList(),
        Markdown = doc.Markdown,
    };
}
```

If `Process.ExtractTop` with default scorer ranks `calc-margins` below 60 for `margins`, pass `scorer: FuzzySharp.SimilarityRatio.ScorerCache.Get<FuzzySharp.SimilarityRatio.Scorer.Composite.WeightedRatioScorer>()` (the default is WeightedRatio, which handles partial matches — expected score ~90). Keep the test as the arbiter.

- [ ] **Step 6: Run handler tests — expect PASS**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests --no-build --filter "FullyQualifiedName~Features.ProcessDocs"
```
Expected: 12 passed.

- [ ] **Step 7: Write failing MCP tool tests** — `MCP/Tools/ProcessDocsMcpToolsTests.cs`

```csharp
using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.ProcessDocs.Contracts;
using Anela.Heblo.Application.Features.ProcessDocs.UseCases.GetProcessDoc;
using Anela.Heblo.Application.Features.ProcessDocs.UseCases.ListProcesses;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class ProcessDocsMcpToolsTests
{
    private static readonly string ReadRole = AccessRoles.For(Feature.Anela_ProcessDocs, AccessLevel.Read);

    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<ILogger<ProcessDocsMcpTools>> _logger = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();

    public ProcessDocsMcpToolsTests()
    {
        _currentUserService.Setup(s => s.IsInRole(ReadRole)).Returns(true);
    }

    private ProcessDocsMcpTools CreateTools() => new(_mediator.Object, _logger.Object, _currentUserService.Object);

    [Fact]
    public async Task ListProcesses_PassesKindAndSerializesResult()
    {
        _mediator.Setup(m => m.Send(It.Is<ListProcessesRequest>(r => r.Kind == "sync"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListProcessesResponse { Processes = [new ProcessSummaryDto { Name = "sync-a" }] });

        var json = await CreateTools().ListProcesses("sync");

        var result = JsonSerializer.Deserialize<ListProcessesResponse>(json, McpJsonOptions.Default);
        Assert.Equal("sync-a", result!.Processes.Single().Name);
    }

    [Fact]
    public async Task GetProcessDoc_Found_ReturnsDoc()
    {
        _mediator.Setup(m => m.Send(It.Is<GetProcessDocRequest>(r => r.Name == "calc-margins"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetProcessDocResponse { Doc = new ProcessDocDto { Name = "calc-margins", Markdown = "# M" } });

        var json = await CreateTools().GetProcessDoc("calc-margins");

        var result = JsonSerializer.Deserialize<ProcessDocDto>(json, McpJsonOptions.Default);
        Assert.Equal("# M", result!.Markdown);
    }

    [Fact]
    public async Task GetProcessDoc_NotFound_ThrowsMcpExceptionWithSuggestions()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetProcessDocRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetProcessDocResponse { Suggestions = ["calc-margins"] });

        var ex = await Assert.ThrowsAsync<McpException>(() => CreateTools().GetProcessDoc("margins"));

        Assert.Contains("calc-margins", ex.Message);
        Assert.Contains("ListProcesses", ex.Message);
    }

    [Fact]
    public async Task ListProcesses_WithoutPermission_Throws()
    {
        _currentUserService.Setup(s => s.IsInRole(ReadRole)).Returns(false);

        await Assert.ThrowsAnyAsync<Exception>(() => CreateTools().ListProcesses(null));
        _mediator.Verify(m => m.Send(It.IsAny<ListProcessesRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

Before writing the last test, read `backend/src/Anela.Heblo.API/MCP/McpAuthorizationExtensions.cs` and `KnowledgeBaseToolsTests.cs` for how an unauthorized call is asserted there, and assert the same exception type instead of `ThrowsAnyAsync<Exception>` if one is used.

- [ ] **Step 8: Implement `ProcessDocsMcpTools.cs`**

```csharp
using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.Application.Features.ProcessDocs.UseCases.GetProcessDoc;
using Anela.Heblo.Application.Features.ProcessDocs.UseCases.ListProcesses;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

[McpServerToolType]
public class ProcessDocsMcpTools
{
    private const string FeatureLabel = "Process docs";

    private readonly IMediator _mediator;
    private readonly ILogger<ProcessDocsMcpTools> _logger;
    private readonly ICurrentUserService _currentUserService;

    public ProcessDocsMcpTools(IMediator mediator, ILogger<ProcessDocsMcpTools> logger, ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _logger = logger;
        _currentUserService = currentUserService;
    }

    [McpServerTool]
    [Description(
        "List Heblo's documented processes — data syncs (external system -> Heblo), calculations (derived numbers " +
        "such as margins, pricing, stock-up) and feeds (Heblo -> outside). Call this FIRST whenever the user asks " +
        "where a number or dataset comes from, how something is calculated, or when/how data gets updated. " +
        "Returns name, kind, one-line summary and related processes; then call GetProcessDoc for the relevant one.")]
    public async Task<string> ListProcesses(
        [Description("Optional filter: 'sync', 'calculation' or 'feed'. Omit to list all.")] string? kind = null,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Anela_ProcessDocs, FeatureLabel);

        var result = await _mediator.Send(new ListProcessesRequest { Kind = kind }, cancellationToken);
        return JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool]
    [Description(
        "Get the full documentation of one Heblo process: purpose, trigger/schedule, data flow, exact formulas, " +
        "configuration, runtime facts, known quirks and code entry points. Follow 'related' processes for upstream " +
        "questions (e.g. where a cost used in a margin comes from). When answering, cite the process name and its " +
        "verifiedAt commit, and treat 'Runtime facts' as true only as of the date written next to each fact.")]
    public async Task<string> GetProcessDoc(
        [Description("Process name exactly as returned by ListProcesses, e.g. 'calc-margins'.")] string name,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Anela_ProcessDocs, FeatureLabel);

        var result = await _mediator.Send(new GetProcessDocRequest { Name = name }, cancellationToken);
        if (result.Doc is null)
        {
            _logger.LogInformation("MCP GetProcessDoc: unknown process '{Name}'", name);
            var hint = result.Suggestions.Count > 0
                ? $" Did you mean: {string.Join(", ", result.Suggestions)}?"
                : string.Empty;
            throw new McpException($"No process named '{name}'.{hint} Call ListProcesses for all names.");
        }

        return JsonSerializer.Serialize(result.Doc, McpJsonOptions.Default);
    }
}
```

Register in `McpModule.cs` after `.WithTools<PricingSimulatorMcpTools>()` (move the `;`):
```csharp
            .WithTools<PricingSimulatorMcpTools>()
            .WithTools<ProcessDocsMcpTools>();
```

- [ ] **Step 9: Run tests — expect PASS**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests --no-build --filter "FullyQualifiedName~ProcessDocs|FullyQualifiedName~ReflectionValidation|FullyQualifiedName~Authorization|FullyQualifiedName~ApplicationStartup"
```
Expected: all pass (the reflection/authorization/startup suites catch a Response not inheriting BaseResponse, an access-matrix inconsistency, or a DI registration miss).

- [ ] **Step 10: Update `docs/integrations/mcp-server.md`**

Change the tool count 28 → 30 wherever it appears, and add a section next to the Knowledge Base one:
```markdown
**Process Docs (2)** — require the `Anela_ProcessDocs` permission.
- `ListProcesses` — index of documented syncs, calculations and feeds (name, kind, summary, related)
- `GetProcessDoc` — full agent-facing doc for one process (data flow, formulas, config, quirks, code entry points); source is `docs/processes/`
```
Also change "28 tools" → "30 tools" in `CLAUDE.md`'s documentation map line for `mcp-server.md`.

- [ ] **Step 11: Commit**

```bash
git add access-matrix.json access-matrix.generated.json access-matrix-entra.generated.json \
        backend/src/Anela.Heblo.Domain/Features/Authorization frontend/src/auth/accessMatrix.generated.ts \
        backend/src/Anela.Heblo.Application/Features/ProcessDocs backend/src/Anela.Heblo.API/MCP \
        backend/test/Anela.Heblo.Tests docs/integrations/mcp-server.md CLAUDE.md
git commit -m "feat: serve process docs through ListProcesses and GetProcessDoc MCP tools"
```

---

### Task 7: CI job, CLAUDE.md rule, `/process` skill, routine doc

**Files:**
- Modify: `.github/workflows/ci-feature-branch.yml`, `CLAUDE.md`
- Create: `.claude/skills/process/SKILL.md`, `docs/routines/process-docs-refresh.md`

**Interfaces:**
- Consumes: `check.py check|pr` (Task 3), docs (Task 4)
- Produces: CI job `process-docs`; routine prompt consumed by the follow-up that creates the routine.

- [ ] **Step 1: Add the CI job**

In `.github/workflows/ci-feature-branch.yml`, under `jobs:` (before `frontend-tests`):
```yaml
  # Process docs - schema, index, orphans (warn during backfill) + stale-doc PR comment
  process-docs:
    name: 📚 Process Docs
    runs-on: ubuntu-latest
    steps:
      - name: 📥 Checkout code
        uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: 🐍 Setup Python
        uses: actions/setup-python@v5
        with:
          python-version: '3.12'

      - name: 📦 Install dependencies
        run: pip install pyyaml pytest

      - name: 🧪 Test the checker
        run: python -m pytest scripts/process-docs -q

      - name: 🔍 Check process docs
        run: python scripts/process-docs/check.py check

      - name: 💬 Flag process docs this PR may have made stale
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          python scripts/process-docs/check.py pr --base "origin/${{ github.base_ref }}" --comment-file process-docs-comment.md
          if [ -f process-docs-comment.md ]; then
            gh pr comment "${{ github.event.pull_request.number }}" --body-file process-docs-comment.md --edit-last --create-if-none \
              || gh pr comment "${{ github.event.pull_request.number }}" --body-file process-docs-comment.md
          fi
```
(`--edit-last` keeps a single comment updated per push; the fallback covers a runner `gh` too old for `--create-if-none`. Note `--edit-last` edits the last comment *by this token's user*, which is `github-actions[bot]` — acceptable since other bot comments come from different workflows' apps; if it overwrites another bot comment in practice, switch to a hidden `<!-- process-docs -->` marker + `gh api` lookup.)

- [ ] **Step 2: Validate workflow YAML**

Run: `python3 -c "import yaml;yaml.safe_load(open('.github/workflows/ci-feature-branch.yml'))" && echo ok`
Expected: `ok`.

- [ ] **Step 3: Add the CLAUDE.md rule and doc-map entry**

In `CLAUDE.md` under **Documentation map**, add a new group after **Integrations**:
```markdown
**Processes** — `docs/processes/INDEX.md` indexes agent-facing docs for every sync, calculation and data feed (where data comes from, how it's computed). Read the relevant one before changing a process.
```
Under **Project-specific rules**, add:
```markdown
- **Process changes update their process doc in the same PR.** Adding or changing a sync, calculation or data feed means creating/updating its `docs/processes/` doc (template: `_TEMPLATE.md`) and running `python3 scripts/process-docs/check.py index`. If behaviour didn't change, bump `verified_at`. Runtime facts found while debugging go into the doc's *Runtime facts* / *Known quirks*, not only into agent memory. CI comments when a PR touches owned code without its doc.
```

- [ ] **Step 4: Write the `/process` skill** — `.claude/skills/process/SKILL.md`

```markdown
---
name: process
description: Answer "where does this data come from / how is this calculated / when does this update" questions about Heblo from the agent-facing process docs. Use when the user says "/process", asks about the origin of a number, a sync, a calculation, a data feed, or how a Heblo process works.
---

# Process docs lookup

Heblo's syncs, calculations and data feeds are documented for agents in `docs/processes/`.

1. Read `docs/processes/INDEX.md` and pick the process(es) that match the question.
2. Read the full doc(s). Follow `related:` links when the question is about an upstream source
   (e.g. a cost used in a margin comes from another process).
3. Answer from the doc. Cite the doc name and its `verified_at` commit. Treat *Runtime facts* as true
   only as of the date written next to each fact — say so if it matters for the answer.
4. If the user wants to go deeper, open the files under *Code entry points*.
5. If no doc covers the question, or the doc contradicts the code you read, say so explicitly and
   offer to write/fix the doc (template: `docs/processes/_TEMPLATE.md`; then run
   `python3 scripts/process-docs/check.py index`).

Non-developers get the same docs through the Heblo MCP tools `ListProcesses` and `GetProcessDoc`.
```

- [ ] **Step 5: Write the routine doc** — `docs/routines/process-docs-refresh.md`

````markdown
# Process Docs Refresh Routine

## Overview

A weekly remote Claude Code routine that keeps `docs/processes/` in step with the code: it refreshes
docs whose owned code changed, re-audits the five least-recently verified docs, and drafts docs for
orphan jobs. It opens one PR per run (never commits to main), which `/automerge-pr` may merge.

## Routine details

| Field | Value |
|---|---|
| Routine ID | _created after the backfill completes — fill in then_ |
| Schedule | Weekly, Monday (`0 4 * * 1` UTC) |
| Model | `claude-sonnet-4-6` |
| Repo | `https://github.com/onpaj/Anela.Heblo` |

## Prompt

```
You maintain Heblo's agent-facing process docs in docs/processes/ (read CLAUDE.md and
docs/processes/_TEMPLATE.md first).

1. pip install pyyaml, then run: python3 scripts/process-docs/check.py check --json
2. For each entry in "stale":
   - reason "changed": read the doc and `git diff <verified_at>..HEAD -- <each owns glob's files>`.
     If behaviour described by the doc changed, edit the doc to match the code. Either way set
     verified_at to the quoted short SHA of HEAD.
   - reason "unknown-commit": fully re-verify the doc against current code, then set verified_at.
3. For each name in "oldest" not already handled: re-verify the whole doc against current code even
   though nothing changed — fix anything wrong, then bump verified_at.
4. For each file in "orphans": write a new doc from the template covering that job (group closely
   related jobs into one doc when they form one process).
5. Never edit "Runtime facts" values you cannot verify from code. List every runtime fact dated more
   than 90 days ago in the PR body under "Runtime facts to re-check".
6. Run: python3 scripts/process-docs/check.py index && python3 scripts/process-docs/check.py check
   — it must pass.
7. If nothing changed, stop without a PR. Otherwise commit on a new branch
   `chore/process-docs-refresh-<date>` and open one PR titled
   "docs: weekly process docs refresh", listing per doc what changed and why.
```
````

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/ci-feature-branch.yml CLAUDE.md .claude/skills/process docs/routines/process-docs-refresh.md
git commit -m "ci: check process docs on PRs; add /process skill and refresh routine doc"
```

---

### Task 8: Full verification and PR

- [ ] **Step 1: Backend gate**

Run:
```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test Anela.Heblo.sln --no-build --filter "Category!=Playwright&Category!=Integration"
```
Expected: build succeeds, format clean, tests pass (AccessMatrixGen crash output during build is known non-fatal noise).

- [ ] **Step 2: Frontend gate** (the generated `accessMatrix.generated.ts` changed)

Run: `cd frontend && CI=false npm run build && npm run lint`
Expected: both succeed. In a fresh worktree run `npm install --legacy-peer-deps` first.

- [ ] **Step 3: Script gate**

Run:
```bash
python3 -m pytest scripts/process-docs -q
python3 scripts/process-docs/check.py check
```
Expected: 31 passed; `OK: 3 process docs` with orphan WARN lines only.

- [ ] **Step 4: Open the PR**

Use the `finishfeature` skill. PR body must include: the spec link, the three exemplar docs as the thing to review for format, the note that orphans are in warn mode, the Docker build result from Task 5 Step 11, and a manual test checklist:
- [ ] after staging deploy, from claude.ai with the Heblo connector: "Where does the cost in M1 come from?", "When does the Flexi analytics sync run and what happens if a page fails?", "How is the stock-up quantity computed?"
- [ ] `anela.process_docs.read` role provisioned (run `scripts/seed-authorization.sh` or the Entra provisioning step the other access-matrix PRs used)

---

## Follow-ups (separate plans, per spec rollout)

1. **Backfill PRs by domain** — Flexi, Shoptet, marketing, margins & finance, stock & manufacture, misc — until `check.py check` shows no orphans. Each batch: draft from code, fold in memory gotchas, trim migrated gotchas from memory. Add non-job calculation files to `config.yaml` `include` as they are documented.
2. **Final PR** — set `orphan_mode: fail` in `config.yaml`; create the weekly routine with the `/schedule` skill using the prompt in `docs/routines/process-docs-refresh.md`, and record its routine ID there.
