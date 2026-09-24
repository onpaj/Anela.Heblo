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
