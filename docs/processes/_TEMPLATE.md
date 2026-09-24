---
process: <prefix>-<name>          # must equal the filename stem; prefix: sync | calc | feed
kind: sync                        # sync | calculation | feed
summary: One sentence — what data this moves or derives, and for whom.
owns:                             # repo-relative globs of the code this doc describes
  - backend/src/**/Feature/**
verified_at: "0000000"            # QUOTED short SHA of the commit this doc was checked against
related: []                       # other process names (upstream or downstream)
---

# <Human title>

## Purpose
Which business question this answers. Who looks at the result, and where (page, report, MCP tool).

## Trigger
Hangfire job id and cron (Europe/Prague), manual trigger in Recurring Jobs, or on-demand (request path).

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
