# Implementation: add-manual-followup-note-for-db-permission-grant

## What was implemented

Documented the one required change this pipeline cannot make itself (spec FR-2): granting
`warehouse.stock_up.read` to the E2E test account's permission group in the staging database. This
ensures the manual step isn't silently dropped once the PR merges.

## Files created/modified
- `artifacts/feat-3540/MANUAL-FOLLOWUP.md` (new) — explains why this can't be automated (separate
  DB-backed permission resolver, not touched by the code fix in tasks 1-2), and gives the repo
  owner exact steps to perform the grant on staging and verify it.

## Tests
Not applicable — documentation only, no code changes.

## How to verify
`cat artifacts/feat-3540/MANUAL-FOLLOWUP.md` — confirm the file is valid markdown with no unclosed
code fences and the steps are accurate against `PermissionResolver.cs` and the `/admin/access` UI.

## Notes
Per the task-context, this content should be surfaced prominently in the PR description (not just
left in `artifacts/`) so it isn't missed — the oneshot pipeline's finishing step will include it in
the PR body under a "Manual follow-up required" heading.

## Status
DONE
