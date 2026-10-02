---
process: module-process-docs
kind: module
module: process-docs
summary: The documentation catalog you are reading — one Markdown doc per Heblo module and process, built into the app and served to Claude through the Heblo MCP tools ListProcesses and GetProcessDoc.
owns: []
verified_at: "f68c439ec"
related: []
---

# Process docs (this catalog)

## Purpose
Anela staff ask Claude (claude.ai connected to the Heblo MCP server) questions like "where does
this margin come from?" or "when is stock sent to Shoptet?". This module gives Claude the
answers: a catalog of written docs, one **module overview** per Heblo area and one doc per
**process** (sync, calculation, feed, job, workflow), each checked against the code at a stated
commit. It is read-only and has no screen; the docs change only through a code change (pull
request), never at runtime.

## Users & screens
No web page. Two MCP tools (`ProcessDocsMcpTools`), both requiring the permission
`Anela_ProcessDocs` ("Procesní dokumentace", read):

| Tool | Input | Returns |
|---|---|---|
| `ListProcesses` | optional `kind` (`module`, `sync`, `calculation`/`calc`, `feed`, `job`, `workflow`/`flow`) and `module` slug, both case-insensitive | name, kind, module, one-line summary, `verifiedAt`, related processes — sorted by name |
| `GetProcessDoc` | `name` (case-insensitive, trimmed) | the full Markdown body plus the same metadata; for an unknown name an error "No process named '…'" with up to 3 fuzzy suggestions (FuzzySharp score ≥ 60) |

Developers and agents read the same files directly in `docs/processes/`; `INDEX.md` there is a
generated table of all docs.

## Processes
None at runtime — the catalog is loaded once from the assembly at start-up (singleton
`EmbeddedProcessDocStore`), with no job and no writes.

How a doc gets into the app and stays current (build/CI side):
1. A doc is a file `docs/processes/<name>.md` with YAML frontmatter (`process`, `kind`,
   `module`, `summary`, `owns`, `verified_at`, `related`). Templates: `_TEMPLATE.md` (process),
   `_TEMPLATE_MODULE.md` (module).
2. `Anela.Heblo.Application.csproj` embeds every `docs/processes/*.md` except `_*.md` and
   `INDEX.md` as resource `ProcessDocs/<file>`; the deployed image therefore serves exactly the
   docs of the commit it was built from.
3. CI (`ci-feature-branch.yml`, job "📚 Process Docs") on every PR: runs the checker's tests,
   `scripts/process-docs/check.py check` (schema, required headings, index drift, `owns` globs
   that match nothing, orphan recurring jobs), and `check.py pr`, which posts/updates one PR
   comment (marker `<!-- process-docs -->`) when the PR changes code owned by a doc without
   touching that doc.
4. Orphans = `IRecurringJob` implementations (plus `include:` files in
   `scripts/process-docs/config.yaml`) not matched by any doc's `owns:` glob.
   `orphan_mode: warn` today; meant to become `fail` once the backfill is complete.
5. A doc is "stale" when files matching its `owns` changed after the later of its
   `verified_at` commit and the doc's own last commit. A weekly refresh routine
   (`docs/routines/process-docs-refresh.md`) is specified to fix stale docs and draft orphan
   docs; its routine id is still "created after the backfill completes".

## Data owned
None in the database. The catalog is the set of Markdown files in `docs/processes/` compiled
into the Application assembly.

## External systems
None. (Consumers reach it through the Heblo MCP endpoint `/mcp`, Entra ID authenticated.)

## Dependencies
- Reads nothing from other modules at runtime. Every other module is *described* by docs here.
- MCP server registration: `McpModule` (`WithTools<ProcessDocsMcpTools>()`).

## Known quirks
- **Malformed docs disappear silently.** A doc whose frontmatter fails to parse (no frontmatter,
  `process` ≠ file name, missing `kind`/`module`/`summary`/`verified_at`, invalid YAML) is
  skipped with an error log; `EmbeddedProcessDocStore.LoadErrors` collects them but nothing reads
  that list, so the tools just don't show the doc. CI's `check.py` catches the same mistakes
  before merge.
- The app's parser checks far less than `check.py` (no heading or `kind` validation) — CI is the
  real gate.
- The permission `Anela_ProcessDocs` has to be granted to a group in each environment
  (`/admin/access`); without it every call fails with an access error (agent memory
  `project_process_docs_catalog`, 2026-09-24).
- Docs reflect the deployed build, not `main`: a doc merged today is visible over MCP only after
  the next deployment.
- `verified_at` and "Runtime facts" are only as fresh as the last edit; Claude is told (tool
  description) to cite `verifiedAt` and the date beside each runtime fact.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/ProcessDocs/EmbeddedProcessDocStore.cs` — loading embedded docs
- `backend/src/Anela.Heblo.Application/Features/ProcessDocs/ProcessDocParser.cs` — frontmatter parsing
- `backend/src/Anela.Heblo.Application/Features/ProcessDocs/UseCases/ListProcesses/ListProcessesHandler.cs` — filters, kind aliases
- `backend/src/Anela.Heblo.Application/Features/ProcessDocs/UseCases/GetProcessDoc/GetProcessDocHandler.cs` — lookup + fuzzy suggestions
- `backend/src/Anela.Heblo.API/MCP/Tools/ProcessDocsMcpTools.cs` — MCP tools and their descriptions
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — `EmbeddedResource` glob
- `scripts/process-docs/check.py`, `scripts/process-docs/config.yaml` — CI checker
- `docs/processes/_TEMPLATE.md`, `docs/processes/_TEMPLATE_MODULE.md` — templates
