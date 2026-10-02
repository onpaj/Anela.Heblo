---
process: <prefix>-<name>          # must equal the filename stem; prefix: sync | calc | feed | job | flow
kind: sync                        # sync | calculation | feed | job | workflow  (prefix flow-)
                                   # sync = external -> Heblo, feed = Heblo -> external,
                                   # calculation = derived numbers, job = other scheduled/background work,
                                   # workflow = user-driven multi-step process with side effects
module: catalog                   # kebab-case module slug; its overview is module-<slug>.md (_TEMPLATE_MODULE.md)
summary: One sentence — what data this moves or derives, and for whom.
owns:                             # repo-relative globs of the code this doc describes
  - backend/src/**/Feature/**
verified_at: "0000000"            # QUOTED short SHA of the commit this doc was checked against.
                                   # Staleness is measured from the LATER of this and the doc's own
                                   # last commit, so editing the doc in the same PR as the code is enough.
related: []                       # other process names (upstream or downstream)
---

# <Human title>

## Purpose
Which business question this answers. Who looks at the result, and where (page, report, MCP tool).

## Trigger
Hangfire job id and cron (Europe/Prague), manual trigger in Recurring Jobs, or on-demand (request path).
For a workflow: who starts it, from which page/button, and the states it moves through.

## Data flow
Source (system / endpoint / table) → numbered steps → target (table / cache / view).
Name real tables, endpoints and cache keys.

## Logic & formulas
Exact rules. State units, with/without VAT, time windows, rounding, what is excluded and why.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|

## Runtime facts
Facts not derivable from code. Each line: fact — source — date checked.
Write "None" if there are none.

## Known quirks
Gotchas, edge cases, historical incidents. One bullet each, cause + effect.

## Code entry points
- `path/to/File.cs` — what to read it for
