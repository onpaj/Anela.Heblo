---
name: hygiene-pr
description: Confirm one PR is mergeable with green CI, back-merging its base branch only when it can't merge as-is, resolving any merge conflict with that base itself, and flagging it needs-work with a comment only when it still can't be fixed — without ever reviewing or merging it. A PR that is merely behind but still mergeable is left alone unless told to force it. Use when the user says "hygiene-pr", "update this PR's branch", "check if PR N is current and green", "resolve conflicts on PR N", "backmerge PR N" (optionally "force"/"no matter what"), or asks to fix one PR's staleness/CI/conflicts without a full review.
---

You bring one PR up to date with its base branch, **resolve any merge
conflict with that base**, and confirm CI is green. Only if it is still
failing, or the conflict is one you cannot resolve, do you flag it
`needs-work` with a comment explaining why — the same durable signal
`/automerge-pr` would leave, so the PR stays discoverable by `/rework-pr`
and by a human even when this runs standalone, independent of whether
`/automerge-pr` ever touches it. You never review or merge.

Conflict resolution is this skill's job, not a reason to hand the PR off:
a stale branch and a conflicting one are the same problem at different
severities, and both block every downstream skill until someone reconciles
them. `/rework-pr` still resolves conflicts as part of *revising* a PR —
that is a different entry point, for a PR whose code was rejected.

**All deterministic work is done by the scripts beside this file.** Your one
judgement call is step 3: what the reconciled content of a conflicting hunk
should be.

**If `USE_GH_API` is set in the environment**, every `gh` invocation shown
below is routed through `.claude/skills/_lib/gh_api.sh` instead -- a
curl+REST equivalent for environments where the `gh` CLI itself is not
permitted (`update_and_wait.sh` and `resolve_conflict.sh` already branch on
it internally).

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
the same eligibility query `automerge-all` uses, opted into conflicted PRs
(they are exactly what step 3 exists to fix, so unlike `/automerge-all` this
skill must see them):

```bash
.claude/skills/automerge-pr/candidates.sh --include-conflicting
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
it's now responsible for to resolution, and — if it ends up `still-failing`
— labels the PR `needs-work` and posts a comment explaining why, via the
shared `_lib/flag_needs_work.sh` helper, which goes through the same
`apply_verdict.sh --action needs-work` mechanism `/automerge-pr` uses for a
code-review rejection. Every other status (`already-clean`, `fixed`,
`conflict`, `ci-running`, `pending-timeout`, `error`) has no side effects
beyond, at most, the `gh pr update-branch` call. Parse its JSON output.

A `conflict` here is **not** a rejection and nothing has been flagged — go
to step 3 and resolve it. Every other status: skip step 3 and report.

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

## 3. Resolve the conflict — only when step 2 said `conflict`

`resolve_conflict.sh` does everything except decide what the reconciled
content of a conflicting hunk should be. Run it in three steps.

```bash
.claude/skills/hygiene-pr/resolve_conflict.sh --pr {N} --step prepare
```

This claims the PR with the `agent-wip` label (the same claim `/rework-pr`
uses, so the two never push to one branch at once), builds or refreshes a
worktree at the PR head, and attempts `git merge origin/{base}`. Branch on
its `status`:

- **`conflicted`** → the `files` array lists exactly what to resolve, inside
  the `worktree` path it reports. Read each one, reconcile the conflicting
  hunks, and remove every conflict marker. Then run `--step finish`.
- **`merged-clean`** → `gh pr update-branch` failed for some reason other
  than a real conflict; the local merge worked. Nothing to edit — go
  straight to `--step finish`.
- **`claimed-elsewhere`** → another `/rework-pr` or `/hygiene-pr` run owns
  this branch. Nothing was touched. Report and stop; do **not** flag
  `needs-work` and do **not** call `--step abort`.
- **`not-open`** → the PR was merged or closed meanwhile. Report and stop.
- **`error`** → an infrastructure failure (the claim, the fetch, the
  worktree). The claim is already released and nothing is half-done. Report
  and stop — this is not a statement about the PR, so do **not** flag it.

**Once `prepare` reports `conflicted` or `merged-clean`, this PR is claimed
by you until `--step finish` reports `pushed` or you run `--step abort`.**
Release it on ANY exit before then — an unexpected error, a tool failure,
running out of turns — by running `--step abort` (or, if the conflict itself
was fine and only something incidental went wrong, `gh pr edit {N} --repo
"$REPO" --remove-label agent-wip`). Nothing sweeps a leaked `agent-wip`
label: it takes the PR out of `/rework-pr`'s backlog permanently, with no
TTL.

Resolve the conflicts the way `/rework-pr` step 4 does: reconcile intent,
never delete one side wholesale to make the merge go away. If a conflict's
intent is genuinely unclear (the same lines changed two incompatible ways
for reasons you can't determine from context), give up explicitly rather
than guessing:

```bash
.claude/skills/hygiene-pr/resolve_conflict.sh --pr {N} --step abort \
  --detail "one line on what could not be reconciled"
```

That cleans up the worktree, releases the claim, and flags the PR
`needs-work` with the same `verdict: REJECT` comment a failing-CI rejection
leaves. Report `conflict-unresolved`.

Otherwise, push what you resolved:

```bash
.claude/skills/hygiene-pr/resolve_conflict.sh --pr {N} --step finish
```

- **`pushed`** → the resolution is on the PR branch, the worktree is gone
  and the claim is released. Now re-run step 2 **with `--force`**, because
  the CI run this push started is yours to wait for (without `--force` the
  script would see it mid-flight and report `ci-running` on a build it is
  in fact responsible for):

  ```bash
  .claude/skills/hygiene-pr/update_and_wait.sh --pr {N} --force
  ```

  Report `conflict-resolved` plus that call's own status and detail.
- **`unresolved`** → conflict markers are still in the files it lists;
  nothing was pushed and the worktree and claim are intact. Fix those files
  and run `--step finish` again. Do this at most twice; if markers survive
  the second attempt, `--step abort` and report `conflict-unresolved`.
- **`push-failed`** → the branch moved under this run and could not be
  reconciled automatically. Run `--step abort --detail "<its detail>"` and
  report `conflict-unresolved`.
- **`error`** → report it and run `--step abort --detail "<its detail>"`
  only if the detail describes the PR rather than infrastructure; when in
  doubt, leave the PR unflagged and say the claim may need releasing by
  hand.

## 4. Report

State the PR number and the `status` field verbatim
(`already-clean` / `fixed` / `still-failing` / `ci-running` /
`pending-timeout` / `error`), plus the `detail` field — or, if step 3 ran,
one of `conflict-resolved` (with the follow-up `update_and_wait.sh` status),
`conflict-unresolved`, `conflict-claimed-elsewhere` or `conflict-not-open`,
with the reason. That is the entire output of this skill — no further action
of your own; the scripts already did everything the outcome requires.

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
| `NEEDS_WORK_LABEL` | `automerge-pr/apply_verdict.sh` — reached via `_lib/flag_needs_work.sh`, which both `update_and_wait.sh` (`still-failing`) and `resolve_conflict.sh` (`--step abort`) call |
| `AGENT_WIP_LABEL` | `resolve_conflict.sh` (must match `rework-pr`'s copies) |
| `PUSH_MAX_ATTEMPTS` | `resolve_conflict.sh` |
| the needs-work comment block (`verdict: REJECT`) | `_lib/flag_needs_work.sh` |

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

A conflict resolution this skill pushes is not reviewed by anything before
it lands on the PR branch — CI is the only check on it, and the merge commit
goes onto a branch a human may later read as if the author wrote it. The
guards are narrow and deliberate: only the files the merge actually
conflicted on are staged, leftover conflict markers are refused
(`unresolved`) rather than pushed, and an unclear conflict is meant to be
`--step abort`ed rather than guessed at. Prefer aborting: a `needs-work` flag
costs one `/rework-pr` round, a wrong reconciliation costs a silent bug.

The `agent-wip` claim shared with `/rework-pr` narrows but does not close
the race — GitHub's label API has no compare-and-set, so two runs reading
"unclaimed" in the same instant can still both proceed. It also does not
cover `update_and_wait.sh` itself: a `gh pr update-branch` from another
sweep can still land mid-resolution, which is what `finish`'s push retry
exists to absorb.

If flagging `needs-work` itself fails (label API outage, permissions), the
underlying `still-failing`/`conflict` status is still reported — the
failure is appended to `detail`, not swallowed — but nothing gets posted to
the PR that run. Re-run to retry the flag.
