---
name: hygiene-all
description: Sweep every open agent PR, bringing each current with its base branch and confirming CI passes — flagging any that are still failing or conflicted as needs-work — without ever reviewing or merging any of them. Use when the user says "hygiene-all", "clean up the PR backlog's branches", "check CI across all open PRs", or wants the backlog kept current independent of /automerge-all ever running.
---

You keep the whole open-PR backlog current with its base branch and confirm
CI status across all of it — independent of review or merge decisions. Any
PR that's still failing or genuinely conflicted after that gets flagged
`needs-work` with an explanatory comment (each `hygiene-pr` subagent does
this itself), so it's discoverable by `/rework-pr` and by a human afterward.
This is safe to run on its own schedule; it never reviews or merges
anything.

## 1. Find the candidates

```bash
.claude/skills/automerge-pr/candidates.sh
```

This is the same eligibility query `/automerge-all` uses (draft, conflicted-
in-the-textual sense via `mergeable`, and already-`needs-work` PRs are
filtered — those aren't this skill's problem to fix). If `candidates` is
empty, print `No agent PRs to check.`, list `skipped` with reasons, and
stop.

## 2. Check each candidate — one subagent per PR, fully in parallel

Spawn **one subagent per candidate PR, all in a single message**, so they
run concurrently — there is no shared resource two `hygiene-pr` runs on
different PRs can collide on. Give each subagent exactly this prompt, with
`{N}` replaced by the PR number:

> Follow `.claude/skills/hygiene-pr/SKILL.md` for PR #{N} in this
> repository. Skip its step 1 (you already have the PR number). Run its
> step 2 and report its step 3's output exactly: the PR number, the
> `status` field, and the `detail` field, as your entire final message —
> nothing else.

## 3. Report

Print a table of every PR: number, status, detail. A `status` of
`still-failing` or `conflict` means that PR was just flagged `needs-work` —
say so plainly in the table rather than making the reader infer it from the
status name. Then list the `skipped` entries from step 1 with their
reasons.
