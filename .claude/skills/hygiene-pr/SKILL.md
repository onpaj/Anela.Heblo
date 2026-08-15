---
name: hygiene-pr
description: Confirm one PR is mergeable with green CI, back-merging its base branch only when it can't merge as-is, and flag it needs-work with a comment if it can't be — without ever reviewing or merging it. A PR that is merely behind but still mergeable is left alone unless told to force it. Use when the user says "hygiene-pr", "update this PR's branch", "check if PR N is current and green", "backmerge PR N" (optionally "force"/"no matter what"), or asks to fix one PR's staleness/CI without a full review.
---

You bring one PR up to date with its base branch and confirm CI is green.
If it's still failing or has a real conflict after that, you flag it
`needs-work` with a comment explaining why — the same durable signal
`/automerge-pr` would leave, so the PR stays discoverable by `/rework-pr`
and by a human even when this runs standalone, independent of whether
`/automerge-pr` ever touches it. You never review or merge.

**All deterministic work is done by the script beside this file.**

**If `USE_GH_API` is set in the environment**, every `gh` invocation shown
below is routed through `.claude/skills/_lib/gh_api.sh` instead -- a
curl+REST equivalent for environments where the `gh` CLI itself is not
permitted (`update_and_wait.sh` already branches on it internally).

## 1. Resolve the target PR

If a PR number was given in your invocation, use it. Otherwise, check
whether the branch you're currently on already has an open PR — if so,
treat it as the target, the same as an explicit number:

```bash
if [ -n "${USE_GH_API:-}" ]; then
  CURRENT_BRANCH=$(git rev-parse --abbrev-ref HEAD)
  .claude/skills/_lib/gh_api.sh pr-view "$CURRENT_BRANCH" 2>/dev/null | jq -r 'select(.state == "OPEN") | .number'
else
  gh pr view --json number,state -q 'select(.state == "OPEN") | .number' 2>/dev/null
fi
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
cannot be merged as it stands — GitHub reports it `BEHIND` (its base
requires branches to be up to date) or `CONFLICTING`. A PR that is merely
some commits behind but still `MERGEABLE`/`CLEAN` is left untouched. If the
invocation explicitly asked to force it (e.g. "force", "no matter what",
`--force`), add `--force`:

```bash
.claude/skills/hygiene-pr/update_and_wait.sh --pr {N} [--force]
```

This single call does everything: reads the PR's current mergeable/behind/
CI state; if CI is already running from some earlier push, reports that
and stops immediately with no side effects at all (not even a branch
update — forcing one would cancel the run in progress) — **unless**
`--force` was passed, which skips that bailout and proceeds anyway.
Otherwise it updates the branch if the PR can't merge as-is, polls the CI
it's now responsible for to resolution, and — if it ends up
`still-failing` or `conflict` — labels the
PR `needs-work` and posts a comment explaining why, via the same
`apply_verdict.sh --action needs-work` mechanism `/automerge-pr` uses for a
code-review rejection. Every other status (`already-clean`, `fixed`,
`ci-running`, `pending-timeout`, `error`) has no side effects beyond, at
most, the `gh pr update-branch` call. Parse its JSON output.

Staleness on its own is **not** a reason to touch a PR. GitHub reports
`mergeStateStatus == "BEHIND"` exactly when being behind blocks the merge —
i.e. the base branch has "require branches to be up to date before merging"
enabled — and that is the only staleness signal acted on by default.
Elsewhere a PR that is 40 commits behind still merges fine, so back-merging
it changes nothing about mergeability while triggering a CI run that the
*next* sweep then sees mid-flight and skips as `ci-running`. That feedback
loop is what made whole backlogs look permanently un-reviewable.

`--force` is what opts into the old behaviour: it additionally consults
`behind_by` from the compare API and updates any PR that is behind at all.
Use it when you actually want master's latest merged into a PR before it's
judged — accepting that it starts a CI run and, if one was already going,
cancels it.

## 3. Report

State the PR number and the `status` field verbatim
(`already-clean` / `fixed` / `still-failing` / `conflict` / `ci-running` /
`pending-timeout` / `error`), plus the `detail` field. That is the entire
output of this skill — no further action of your own; the script already
did everything `still-failing`/`conflict` require.

`ci-running` means CI was already mid-flight when this run looked, from a
push this run had nothing to do with, on a PR that needs nothing else done
to it. There is nothing useful to do but wait, and this run declines to
spend its poll window waiting on a build it didn't start — it touches
nothing and returns. Re-run later, once that CI has settled, or pass
`--force` to poll it through to resolution instead.

`error` means the GitHub API call itself failed (auth expiry, rate limit,
network) — it is not a statement about the PR at all, and it is never
flagged `needs-work`. The script stops immediately rather than polling, so
an infrastructure failure never masquerades as a `pending-timeout`. Retry
once the underlying problem is fixed.

## Constants

| Constant | Where it lives |
|----------|----------------|
| `HYGIENE_POLL_INTERVAL_SECONDS`, `HYGIENE_POLL_MAX_ATTEMPTS`, `HYGIENE_NO_CHECKS_GRACE_ATTEMPTS` | `update_and_wait.sh` (env-overridable) |
| `NEEDS_WORK_LABEL` | `automerge-pr/apply_verdict.sh` — `update_and_wait.sh` calls it for the `still-failing`/`conflict` flag |

## Limits worth knowing

The poll window is bounded — a PR whose CI genuinely takes longer than
`HYGIENE_POLL_MAX_ATTEMPTS × HYGIENE_POLL_INTERVAL_SECONDS` reports
`pending-timeout`, not failure. Run this skill again later to re-check.
That poll loop only ever runs for CI this run itself triggered (via
`gh pr update-branch`) — CI already running when this run started reports
`ci-running` and returns immediately, no polling at all, *unless* `--force`
was passed. Default (no `--force`) is the right mode for a scheduled
sweep — it only touches a PR that cannot merge as it stands, so a sweep
never starts CI runs that the following sweep then skips on.

When it does update a branch, it waits for the new head's checks to be
*created* (up to `HYGIENE_NO_CHECKS_GRACE_ATTEMPTS` polls) before reading
them, because GitHub reports an empty check rollup for the first seconds
after a push. Without that wait it reported `fixed` on CI that had not
started, which both let `/automerge-pr` merge on unverified checks and left
a run in flight for the next sweep to skip on. If no checks ever appear the
grace window expires and the PR is `fixed` — that's a repo with no PR CI,
which is legitimate.

`conflict` means `gh pr update-branch` could not resolve a real merge
conflict — that needs judgement. This skill does not attempt one; it just
flags the PR `needs-work` so it's visible. A human or `/rework-pr` (which
does real conflict resolution as part of revising a PR) is the next step.

If flagging `needs-work` itself fails (label API outage, permissions), the
underlying `still-failing`/`conflict` status is still reported — the
failure is appended to `detail`, not swallowed — but nothing gets posted to
the PR that run. Re-run to retry the flag.
