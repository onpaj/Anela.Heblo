---
name: chopchop
description: Stop loafing and pick up the next piece of work. Finds the oldest open GitHub issue labelled `agent` that has no PR yet and runs the oneshot pipeline on it — exactly one issue per invocation, then stops. Use when the user says "chopchop", "do some work", "get to work", "next issue", "pick up the next task", or otherwise tells the harness to stop being lazy and ship something.
---

You are the "get off your ass and do work" skill. Your job: find the single
oldest open issue that still needs work, then kick off the `oneshot` pipeline on
it. No feature ID required from the user — you go find the work yourself.

**Hard rule: exactly one issue per invocation, start to finish.** Once your
claim in step 3 succeeds, you are committed to that issue for the rest of
this run — and that issue's outcome, whatever it is, ends the run. (Walking
past taken candidates and lost claim races in steps 2–3 is part of picking,
not a violation.) You must not, under any circumstances:
- go back to step 1 or re-run the candidate list after a successful claim,
- pick up, glance at, or start work on any other issue while this one is
  in flight, even if oneshot's pipeline pauses, hands control back to you
  between phases, or finishes faster than expected,
- treat "oneshot said the pipeline is running autonomously" as permission
  to consider this invocation done and go find more work,
- start another issue after this one **ends** — and every way it can end
  counts: PR opened successfully, pipeline failed, tests couldn't be fixed,
  a phase reported BLOCKED, oneshot stopped early, a tool call errored, or
  you gave up. Success and failure terminate the invocation equally. A
  failure on the claimed issue is **reported**, never "compensated for" by
  grabbing a different issue.

Step 5 does not fire-and-forget oneshot — it drives that issue's entire
pipeline (via the orchestrator agent, through to PR creation and the
`agent-completed` label) inside this same invocation. This invocation's job
ends only when that one issue is fully handled — done, or blocked/failed and
reported to the user. If you want to work on another issue, that is a
**new** `/chopchop` invocation, never a continuation of this one.

## What you do

**If `USE_GH_API` is set in the environment**, every `gh` invocation shown
below is routed through `.claude/skills/_lib/gh_api.sh` instead -- a
curl+REST equivalent for environments where the `gh` CLI itself is not
permitted.

1. **List candidate issues.** Get all open issues labelled `agent`, oldest
   first, using the `gh` CLI:
```bash
if [ -n "${USE_GH_API:-}" ]; then
  .claude/skills/_lib/gh_api.sh issue-list open agent | jq 'sort_by(.createdAt)'
else
  gh issue list --label agent --state open --json number,title,createdAt \
    --limit 200 --search "sort:created-asc"
fi
```
   If the list is empty, tell the user there's nothing to do ("No `agent` issues
   waiting — you're all caught up.") and stop.

2. **Find the oldest unstarted one.** Walk the list from oldest to newest. For
   each issue number `N`, the oneshot pipeline uses a branch named
   `feature/{N}-{slug}`, so an issue is already taken if **either** a remote
   branch or a PR with that prefix exists. Check the branch first (cheap, no
   API quota — and it catches pipelines that are mid-flight but haven't opened
   their PR yet):
```bash
git ls-remote --heads origin "feature/${N}-*"
```
   Non-empty output → taken, skip to the next-oldest. If empty, also check for
   a PR whose head branch was since deleted:
```bash
if [ -n "${USE_GH_API:-}" ]; then
  REPO=$(.claude/skills/_lib/gh_api.sh repo)
  .claude/skills/_lib/gh_api.sh paginate "repos/$REPO/pulls?state=all" \
    | jq "[.[] | select(.head.ref | startswith(\"feature/${N}-\"))] | length"
else
  gh pr list --state all --json number,headRefName \
    --jq "[.[] | select(.headRefName | startswith(\"feature/${N}-\"))] | length"
fi
```
   - If both checks come up empty, this issue is a **candidate** — try to claim
     it (next step).
   - Otherwise skip it and move to the next-oldest.

   If every `agent` issue is already taken, tell the user there's nothing left
   to start ("Every `agent` issue already has a branch or PR.") and stop.

3. **Claim the candidate atomically.** Listing and checking are racy — another
   chopchop running in parallel may have picked the same issue. The claim
   script creates the remote `feature/{N}-{slug}` branch through the GitHub
   refs API, which is a true test-and-set: exactly one concurrent claimer
   succeeds. It also swaps the labels `agent` → `agent-wip` for visibility.
```bash
BRANCH=$(.claude/skills/oneshot/claim_issue.sh "$N")
```
   - Exit `0` — you own the issue; `$BRANCH` holds the claimed branch name.
   - Exit `3` — another runner claimed it between your check and your claim.
     **Not an error**: go back to step 2 and continue walking to the
     next-oldest candidate.
   - Any other exit — a real failure; report it and stop.

4. **Announce the pick.** Print which issue you selected, e.g.
   `Picking up the oldest unstarted issue: #{N} — {title}`.

5. **Run oneshot on it.** Invoke the `oneshot` skill on the selected issue
   number — this drives the full pipeline (worktree on `feature/{N}-{slug}`,
   label lifecycle `agent` → `agent-wip` → `agent-completed`, tests, in-pipeline
   code review, push, and the `agent`-labelled PR):
```
/oneshot {N}
```
   Follow the oneshot skill's instructions end to end; do not duplicate its
   steps here. The issue is **already claimed** — tell oneshot to skip its own
   claim step and attach to the existing `$BRANCH` on origin.

6. **Stop.** Report the outcome of the one claimed issue — the PR URL on
   success, or what failed/blocked and where things were left — and end the
   invocation. This step is terminal on **every** path: after a success,
   after a failure, and after an early abort alike. Do not list issues
   again, do not claim again, do not start, resume, or "quickly check"
   any other issue. The user runs `/chopchop` again when they want the next
   one.

## Notes

- Only **one** issue is picked per invocation — the oldest eligible one. Run the
  skill again to grab the next.
- "Oldest" is by issue creation time (`createdAt`), ascending.
- **Parallel-safe.** The lock is the remote `feature/{N}-{slug}` branch itself,
  created atomically by `claim_issue.sh` — labels and listings are advisory
  only. Multiple chopchop invocations racing over the same backlog each end up
  with a different issue (or with "nothing left").
- **Stale claims.** If a run dies after claiming, the issue is left with an
  `agent-wip` label and a `feature/{N}-*` branch containing no commits beyond
  the default branch. To release it, delete the remote branch and restore the
  `agent` label:
  `git push origin --delete {branch} && gh issue edit {N} --add-label agent --remove-label agent-wip`
  (or `.claude/skills/_lib/gh_api.sh issue-edit {N} --add-label agent --remove-label agent-wip`
  under `USE_GH_API`)
- Step 1's list can be stale by the time you reach step 3 — that's expected
  and harmless: the atomic claim is the real gate, and a lost race (exit
  `3`) simply moves you to the next candidate. Because you claim **before**
  invoking oneshot, oneshot's own claim step is skipped (see its step 3) —
  the claim never gets re-run against itself.
