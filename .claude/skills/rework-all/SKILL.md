---
name: rework-all
description: Revise every open needs-work PR under the revision-attempt cap, one rework-pr run per PR, fully in parallel. Use when the user says "rework-all", "revise all needs-work PRs", "clear the needs-work backlog", or asks to act on every rejected agent PR at once.
---

You autonomously revise every open `needs-work` PR that hasn't hit the
revision-attempt cap, running `/rework-pr` against each one. Unlike
`/automerge-all`, there is no serialization step here: each PR lives on its
own branch/worktree, and `list_candidates.sh` returns at most one row per
PR number, so within a single `/rework-all` run no two of its own
subagents ever target the same PR — the whole batch runs fully in
parallel, start to finish. The `agent-wip` claim `/rework-pr` takes (its
SKILL.md step 2, including its live-label recheck immediately before
claiming) is what protects against a *different* concurrent run — another
`/rework-all`, a direct `/rework-pr {N}`, or a scheduled one — converging
on the same PR; it narrows that race but is not a true lock (see that
skill's own caveat).

## 1. Find the candidates

```bash
.claude/skills/rework-pr/list_candidates.sh
```

Capped at 20 PRs per run (`MAX_CANDIDATES` in `list_candidates.sh`) — the
same fan-out safety bound `/automerge-all` uses. If `candidates` is empty,
print `No needs-work PRs ready to revise.`, list `skipped` with reasons,
and stop.

## 2. Revise each candidate — one subagent per PR, fully in parallel

Spawn **one subagent per candidate PR, all in a single message**, so they
run concurrently. Give each subagent exactly this prompt, with `{N}`
replaced by the PR number:

> Follow `.claude/skills/rework-pr/SKILL.md` for PR #{N} in this
> repository, end to end, including its own commit and push. Skip its
> step 1 (you already have the PR number: {N}). Report exactly what its
> step 8 asks for as your entire final message.

## 3. Report

Print a table of every PR: number, summary of what was changed (or why it
was skipped: not-open, conflict resolution declined, push retries
exhausted). Then list the `skipped` entries from step 1 with their reasons,
and if `truncated` > 0, state exactly how many eligible PRs were left
unprocessed this run — a PR sitting at the revision cap needs a human to
look at it either way.

## Constants

Do not restate these values elsewhere; each lives in exactly one file.

| Constant | Where it lives |
|----------|----------------|
| `MAX_REVISION_ATTEMPTS`, `AGENT_WIP_LABEL`, `MAX_CANDIDATES` | `rework-pr/list_candidates.sh` |

## Limits worth knowing

If two candidates from step 1 happen to touch the same underlying issue (not
the same PR — the `agent-wip` claim already rules that out) in ways that
interact outside git (e.g. both regenerating the same generated file via an
external tool), running them fully in parallel could still produce
surprising results. This is not covered by this design; it hasn't been
observed and isn't fixed here.
