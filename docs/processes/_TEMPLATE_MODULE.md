---
process: module-<slug>            # must equal the filename stem: module-<slug>
kind: module
module: <slug>                    # kebab-case; every process doc of this module uses the same value
summary: One sentence — what this module does for the business.
owns: []                          # module docs own no code; their process docs do
verified_at: "0000000"            # QUOTED short SHA of the commit this doc was checked against
related: []                       # process names this module's work depends on or feeds (any module)
---

# <Module name>

## Purpose
What this module does for Anela, in business terms. Which question or job it serves, and for whom.

## Users & screens
Who uses it, the pages (route + what they do there), dashboard tiles, MCP tools.

## Processes
One line per process doc of this module: `process-name` — what it does, trigger. Then list user
actions that are plain CRUD (no doc needed). Every scheduled job and every user action that writes
to an external system must have a process doc.

## Data owned
Tables, schemas, caches and blob containers this module writes. One line each: what a row means.

## External systems
Each external system (Flexi, Shoptet, Microsoft 365, …) the module reads or writes, which
endpoints/queries, and the direction.

## Dependencies
Other Heblo modules it reads from, and which modules read from it.

## Known quirks
Gotchas, dead features, gaps. One bullet each, cause + effect. Write "None" if there are none.

## Code entry points
- `path/to/Module.cs` — what to read it for
