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
