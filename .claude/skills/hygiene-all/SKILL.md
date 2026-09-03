---
name: hygiene-all
description: Sweep every open PR — labelled or not, whoever opened it — confirming each is mergeable with green CI and resolving any merge conflicts with its base, flagging as needs-work only the ones still failing or beyond resolving, without ever reviewing or merging any of them. Back-merges only the PRs that cannot merge as they stand, unless told to force it regardless. Use when the user says "hygiene-all", "clean up the PR backlog's branches", "check CI across all open PRs", "resolve conflicts across open PRs", "backmerge all" (optionally "force"/"no matter what"), or wants the backlog kept current independent of /automerge-all ever running.
---

You keep the whole open-PR backlog current with its base branch, resolve
the conflicts that stop it from getting there, and confirm CI status across
all of it — independent of review or merge decisions. "Whole backlog" is
literal: every open PR, not just the pipeline's `agent`-labelled ones. A
stale branch, a merge conflict and a red build are the same problem
whoever opened the PR, and an unlabelled PR is the one most likely to rot
unnoticed precisely because no other skill looks at it. Any PR still failing,
or whose conflict could not be resolved, gets flagged `needs-work` with an
explanatory comment (each `hygiene-pr` subagent does this itself), so it's
discoverable by `/rework-pr` and by a human afterward. This is safe to run
on its own schedule; it never reviews or merges anything.

By default, a PR is only back-merged when it cannot merge as it stands
(GitHub reports it `BEHIND` or `CONFLICTING`); one that is merely some
commits behind but still mergeable is left untouched. This is the right
mode for a scheduled sweep (e.g. hourly): it starts no CI runs that the
next sweep would then see mid-flight and skip. Only add `--force` — which
back-merges any PR that is behind at all, starting (or cancelling and
restarting) CI on it — if the invocation explicitly asked for it (e.g.
"force", "no matter what"). That is for an explicit, on-demand run, not the
schedule.

## GitHub access

Every subagent you spawn reads and writes GitHub through the **`github` MCP
server** (`mcp__github__*`), per `hygiene-pr`/SKILL.md`'s *GitHub access* section,
falling back to `.claude/skills/_lib/gh_api.sh` (curl+REST) when MCP is not
connected — never to the `gh` CLI, which is blocked wherever this runs
unattended. The scripts this skill calls directly keep their own
`gh` / `USE_GH_API` transport, unchanged; set `USE_GH_API=1` in any
environment without `gh`.

## 1. Find the candidates

```bash
.claude/skills/automerge-pr/candidates.sh --include-conflicting --all-open
```

Same eligibility query `/automerge-all` uses, minus two of its filters:

- `--all-open` drops the `agent` label requirement, so an unlabelled or
  human-opened PR is a candidate too. Without it those PRs are invisible to
  every skill in this family — they do not even reach `skipped`.
- `--include-conflicting` keeps `CONFLICTING` PRs, because resolving that
  conflict is exactly what each subagent is being sent to do.

Draft, `UNKNOWN`-mergeability, `CHANGES_REQUESTED`, already-`needs-work` and
`human-required` PRs are still skipped — those aren't this skill's problem
to fix. If `candidates` is empty, print `No PRs to check.`, list `skipped`
with reasons, and stop.

Because the sweep now reaches PRs a person may be actively working on, each
subagent pushes only a merge commit from the base branch and never rewrites
history — `hygiene-pr` already guarantees that, and it is what makes running
this over someone's in-flight branch safe.

## 2. Check each candidate — one subagent per PR, fully in parallel

Spawn **one subagent per candidate PR, all in a single message**, so they
run concurrently — two `hygiene-pr` runs on different PRs work in separate
per-branch worktrees and claim different PRs, so they have no shared state
to collide on. Give each subagent exactly this prompt, with `{N}` replaced
by the PR number:

> Follow `.claude/skills/hygiene-pr/SKILL.md` for PR #{N} in this
> repository. Skip its step 1 (you already have the PR number). Run its
> step 2{FORCE_CLAUSE}, then its step 3 if and only if step 2 reported
> `conflict`, and report its step 4's output exactly: the PR number, the
> `status` field, and the `detail` field, as your entire final message —
> nothing else.

`{FORCE_CLAUSE}` is `" with --force"` if this invocation asked to force
the backmerge, otherwise empty — substitute it literally into the prompt
above so every subagent gets the same mode.

## 3. Report

Print a table of every PR: number, status, detail. A `status` of
`still-failing` or `conflict-unresolved` means that PR was just flagged
`needs-work`, and `conflict-resolved` means a merge resolution was pushed to
that PR's branch — say both plainly in the table rather than making the
reader infer them from the status name. Then list the `skipped` entries from
step 1 with their reasons.
