---
name: hygiene-pr
description: Bring one PR's branch current with its base branch, confirm CI passes, and flag it needs-work with a comment if it can't be — without ever reviewing or merging it. Backmerges only if the PR actually needs it unless told to force it regardless. Use when the user says "hygiene-pr", "update this PR's branch", "check if PR N is current and green", "backmerge PR N" (optionally "force"/"no matter what"), or asks to fix one PR's staleness/CI without a full review.
---

You bring one PR up to date with its base branch and confirm CI is green.
If it's still failing or has a real conflict after that, you flag it
`needs-work` with a comment explaining why — the same durable signal
`/automerge-pr` would leave, so the PR stays discoverable by `/rework-pr`
and by a human even when this runs standalone, independent of whether
`/automerge-pr` ever touches it. You never review or merge.

**All deterministic work is done by the script beside this file.**

## 1. Resolve the target PR

If a PR number was given in your invocation, use it. Otherwise, check
whether the branch you're currently on already has an open PR — if so,
treat it as the target, the same as an explicit number:

```bash
gh pr view --json number,state -q 'select(.state == "OPEN") | .number' 2>/dev/null
```

If that prints a number, use it as `{N}` and skip the candidate search
below. Otherwise, find the oldest open `agent`-labelled PR by number using
the same eligibility query `automerge-all` uses:

```bash
.claude/skills/automerge-pr/candidates.sh
```

Take the lowest `.number` from `.candidates`. If `candidates` is empty,
print `No agent PRs to check.` and stop.

## 2. Run the check

By default, the backmerge (`gh pr update-branch`) only happens if this PR
actually needs one — behind its base or conflicting. If the invocation
explicitly asked to force it (e.g. "force", "no matter what", `--force`),
add `--force`:

```bash
.claude/skills/hygiene-pr/update_and_wait.sh --pr {N} [--force]
```

This single call does everything: reads the PR's current mergeable/behind/
CI state; if CI is already running from some earlier push, reports that
and stops immediately with no side effects at all (not even a branch
update — forcing one would cancel the run in progress) — **unless**
`--force` was passed, which skips that bailout and proceeds anyway,
accepting that it cancels the in-flight run. Otherwise it updates the
branch if it's actually behind or conflicting (never happens under
`--force` alone — `--force` only overrides the CI-running bailout, it
never triggers an update-branch call on a PR that's already current, since
there'd be nothing to merge), polls the CI it's now responsible for to
resolution, and — if it ends up `still-failing` or `conflict` — labels the
PR `needs-work` and posts a comment explaining why, via the same
`apply_verdict.sh --action needs-work` mechanism `/automerge-pr` uses for a
code-review rejection. Every other status (`already-clean`, `fixed`,
`ci-running`, `pending-timeout`, `error`) has no side effects beyond, at
most, the `gh pr update-branch` call. Parse its JSON output.

Staleness is decided from two independent signals: GitHub's
`mergeStateStatus == "BEHIND"`, **and** `behind_by` from the compare API.
The second one matters — GitHub only ever reports `BEHIND` when the base
branch has "require branches to be up to date before merging" enabled, so
on a repo with no branch protection the compare check is the *only* signal
that fires.

## 3. Report

State the PR number and the `status` field verbatim
(`already-clean` / `fixed` / `still-failing` / `conflict` / `ci-running` /
`pending-timeout` / `error`), plus the `detail` field. That is the entire
output of this skill — no further action of your own; the script already
did everything `still-failing`/`conflict` require.

`ci-running` means CI was already mid-flight when this run looked, from a
push this run had nothing to do with — it deliberately does not update the
branch or poll in that case, since an update-branch call would cancel the
in-progress run for no benefit. Re-run later, once that CI has settled.

`error` means the GitHub API call itself failed (auth expiry, rate limit,
network) — it is not a statement about the PR at all, and it is never
flagged `needs-work`. The script stops immediately rather than polling, so
an infrastructure failure never masquerades as a `pending-timeout`. Retry
once the underlying problem is fixed.

## Constants

| Constant | Where it lives |
|----------|----------------|
| `HYGIENE_POLL_INTERVAL_SECONDS`, `HYGIENE_POLL_MAX_ATTEMPTS` | `update_and_wait.sh` (env-overridable) |
| `NEEDS_WORK_LABEL` | `automerge-pr/apply_verdict.sh` — `update_and_wait.sh` calls it for the `still-failing`/`conflict` flag |

## Limits worth knowing

The poll window is bounded — a PR whose CI genuinely takes longer than
`HYGIENE_POLL_MAX_ATTEMPTS × HYGIENE_POLL_INTERVAL_SECONDS` reports
`pending-timeout`, not failure. Run this skill again later to re-check.
That poll loop only ever runs for CI this run itself triggered (via
`gh pr update-branch`) — CI already running when this run started reports
`ci-running` and returns immediately, no polling at all, *unless* `--force`
was passed, in which case it cancels that run and proceeds anyway. Default
(no `--force`) is the right mode for a scheduled sweep — it only ever
touches a PR that actually needs it and never fights a build already in
flight. `--force` is for an explicit, on-demand "do it anyway" request; it
still never invokes `gh pr update-branch` on a PR with nothing to merge.

`conflict` means `gh pr update-branch` could not resolve a real merge
conflict — that needs judgement. This skill does not attempt one; it just
flags the PR `needs-work` so it's visible. A human or `/rework-pr` (which
does real conflict resolution as part of revising a PR) is the next step.

If flagging `needs-work` itself fails (label API outage, permissions), the
underlying `still-failing`/`conflict` status is still reported — the
failure is appended to `detail`, not swallowed — but nothing gets posted to
the PR that run. Re-run to retry the flag.
