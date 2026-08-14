# Implementation: file-dead-export-tooling-followup-issue

## What was implemented

Filed the required follow-up issue and linked it from the PR body, per arch review Decision 6 / A-5.

- **Duplicate check**: `gh_api.sh` has no search subcommand and the GitHub Search API (`search/issues`) returned HTTP 403 ("sessions are bound to their configured repositories") under this repo-scoped token, so I listed all open issues (`GET repos/onpaj/anela.heblo/issues?state=open&per_page=100`, ~17 open issues) and eyeballed titles for "knip" / "ts-prune" / "dead export" / "unused". None matched — no duplicate exists.
- **Created issue #3927**: [`frontend: add a dead-export detector (knip) to catch unreachable modules`](https://github.com/onpaj/Anela.Heblo/issues/3927) — title and body copied verbatim from the task-context file's Step 2 heredoc. No labels were applied (the task-context's exact `gh issue create` command carries no `--label` flag).
- **Updated PR #3907** (`feature/3889-Arch-Review-Transportboxes-Usetransportboxtransiti` → `main`, already existed, currently draft): fetched the existing body first, then appended the three-paragraph follow-up block from the task-context file's Step 3, substituting `#3927` for `<ISSUE_NUMBER>`. All prior body content (motivation, planned units of work, artifacts section, generated-by footer) was preserved unchanged above the appended block.

## Files created/modified

(none — this task is GitHub-only)

## Tests

N/A

## How to verify

- Issue: https://github.com/onpaj/Anela.Heblo/issues/3927 — body matches the task-context Step 2 heredoc exactly.
- PR: https://github.com/onpaj/Anela.Heblo/pull/3907 — body now ends with:
  > Follow-up (required, arch review A-5): #3927 — add a frontend dead-export detector so this class of finding is caught automatically.
  >
  > Not gating on E2E: ...
  >
  > Not implementing the missing endpoint: ...
- `git status --short` in the worktree shows no changes under `frontend/` or `docs/` (see Notes for the one unrelated file that did change).

## Notes

- No duplicate issue was found via reasonable-effort search (open-issue title scan, since the GitHub Search API path is blocked for this token); issue #3927 was created fresh.
- No `--label` was passed to `issue-create` because the task-context file's literal Step 2 `gh issue create` command does not specify one.
- PR #3907 already existed for this branch (opened during planning), so Step 3 appended to its existing body rather than creating a new PR — no PR was created by this task.
- `git status --short` reports one incidental change: `artifacts/feat-3889/state.json` (pipeline-owned task-status tracking, updated automatically when this task started — `developing` timestamp and this task's status flipped to `in_progress`). This is harness bookkeeping, not a change made by this task's work, and matches the task's own note that no `frontend/` or `docs/` changes should exist.

## PR Summary

This task made no code changes — it is a purely GitHub-side operation that files a required follow-up issue (#3927, proposing a `knip`/`ts-prune` dead-export detector for the frontend CI pipeline) and appends the mandated arch-review follow-up/scope-justification text to PR #3907's description.
