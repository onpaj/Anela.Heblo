# `gh_api.sh paginate` fails on comment threads over ~128KB per page

`req_paginate()` in `.claude/skills/_lib/gh_api.sh` used to build up accumulated
pages with:

```bash
all=$(jq -c -n --argjson a "$all" --argjson b "$body" '$a + $b')
```

`--argjson` passes `$body` as a single `execve()` argument. Linux caps any single
argument at `MAX_ARG_STRLEN` (~128KB), independent of the much larger total
`ARG_MAX` — so any single comments page whose JSON serializes past that (e.g.
PR #4153's 55 comments, ~336KB) fails with `jq: Argument list too long`, even
though only one page (no real pagination) was needed.

This broke `list_candidates.sh` outright (used by `/rework-all`), returning an
empty `candidates` list with no clear error surfaced to the caller — it looked
like "no needs-work PRs ready to revise" rather than a tooling failure.

**Fix applied (2026-09-24):** route both operands through temp files and
`jq -c -s '.[0] + .[1]'` instead of `--argjson`, which has no per-argument size
limit. Pushed directly to `.claude/skills/_lib/gh_api.sh` on
`claude/focused-lamport-ifck18`.

**Risk of recurrence:** like the `context_files` glob regression documented in
`agent-context-files-superpowers-plugin-missing.md`, `agentharness init --force`
(run by the SessionStart hook) may overwrite this file from the upstream
`harness` package template on a future session start and silently reintroduce
the bug. If it recurs, the durable fix belongs upstream in `onpaj/harness`.
