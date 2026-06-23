---
name: chopchop
description: Stop loafing and pick up the next piece of work. Finds the oldest open GitHub issue labelled `agent` that has no PR yet and runs the oneshot pipeline on it. Use when the user says "chopchop", "do some work", "get to work", "next issue", "pick up the next task", or otherwise tells the harness to stop being lazy and ship something.
---

You are the "get off your ass and do work" skill. Your job: find the single
oldest open issue that still needs work, then kick off the `oneshot` pipeline on
it. No feature ID required from the user — you go find the work yourself.

## What you do

1. **List candidate issues.** Get all open issues labelled `agent`, oldest
   first, using the `gh` CLI:
```bash
gh issue list --label agent --state open --json number,title,createdAt \
  --limit 200 --search "sort:created-asc"
```
   If the list is empty, tell the user there's nothing to do ("No `agent` issues
   waiting — you're all caught up.") and stop.

2. **Find the oldest one without a PR.** Walk the list from oldest to newest. For
   each issue number `N`, the oneshot pipeline uses a branch named
   `feature/{N}-{slug}`, so check whether any PR's head branch starts with
   `feature/{N}-`:
```bash
gh pr list --state all --json number,headRefName \
  --jq "[.[] | select(.headRefName | startswith(\"feature/${N}-\"))] | length"
```
   - If the result is `0`, this issue has **no PR** — it's your target. Stop
     walking.
   - If the result is `>= 1`, a PR already exists; skip this issue and move to
     the next-oldest.

   If every `agent` issue already has a PR, tell the user there's nothing left to
   start ("Every `agent` issue already has a PR open.") and stop.

3. **Announce the pick.** Print which issue you selected, e.g.
   `Picking up the oldest unstarted issue: #{N} — {title}`.

4. **Run oneshot on it.** Invoke the `oneshot` skill on the selected issue
   number — this drives the full pipeline (worktree on `feature/{N}-{slug}`,
   label lifecycle `agent` → `agent-wip` → `agent-completed`, tests, in-pipeline
   code review, push, and the `agent`-labelled PR):
```
/oneshot {N}
```
   Follow the oneshot skill's instructions end to end; do not duplicate its
   steps here.

## Notes

- Only **one** issue is picked per invocation — the oldest eligible one. Run the
  skill again to grab the next.
- "Oldest" is by issue creation time (`createdAt`), ascending.
- The PR check keys off the `feature/{N}-{slug}` branch convention that
  `oneshot` uses (matched by the `feature/{N}-` prefix). An issue that's
  mid-flight will already have a PR (or be labelled `agent-wip`, which removes
  its `agent` label), so it won't be picked again.
