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
