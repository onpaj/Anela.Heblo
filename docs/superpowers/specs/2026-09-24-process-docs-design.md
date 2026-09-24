# Process Docs — Design

**Date:** 2026-09-24
**Status:** Approved design, pending implementation plan

## Problem

Heblo has ~61 recurring jobs plus a set of calculations (margins M0–M3, pricing, stock-up,
Financial Overview, manufacture write-down, …). Nobody — neither the developer nor the business
owners — can reliably answer "where does this number come from?" or "how is this calculated?"
without re-reading code. The knowledge that exists is scattered across `docs/features/` (a mix of
specs, plans and change specs), personal agent memory (gotchas), and people's heads.

## Goal

A catalog of **process docs** — one per sync, calculation or data feed — written for AI agents,
kept current by a mix of PR-time checks and a weekly agent routine, and queryable by:

- the developer from Claude Code, and
- non-dev colleagues from claude.ai via the existing Heblo MCP connector.

Success: a colleague asks Claude "where does the cost in M1 come from?" and gets a correct,
cited answer without anyone reading code; and a PR that changes a process cannot silently leave
its doc behind.

## Non-goals

- End-user documentation (UI help, how-to guides).
- Using the Knowledge Base RAG. It is PDF-only, uses an inbox→archive model that cannot update a
  changed document, and has an open `/me/drive` 403 issue; chunked retrieval also breaks
  "read the whole calculation" questions.
- Publishing to OneDrive/SharePoint. The repo is the single source of truth; the MCP serves it.
- Verifying runtime facts (prod config values, data observations) automatically.

## 1. The catalog

**Location:** `docs/processes/`

**What counts as a process** — anything that moves or derives data:

| Kind | Meaning | Examples |
|---|---|---|
| `sync` | External system → Heblo | Flexi ledger, Shoptet orders, Ecomail, GA4, Meta/Google Ads |
| `calculation` | Derived numbers | Margins M0–M3, pricing, stock-up, Financial Overview, sklad výroby write-down |
| `feed` | Heblo → outside | Metabase views, `shoptet_raw` mirror, exports |

**File naming:** `<kind-prefix>-<name>.md` where prefix is `sync`, `calc` or `feed`
(e.g. `sync-flexi-ledger.md`, `calc-margins.md`, `feed-metabase-views.md`).

**Frontmatter:**

```yaml
---
process: flexi-analytics-sync
kind: sync                     # sync | calculation | feed
summary: Mirrors the Flexi accounting ledger and contacts into flexi_raw for reporting.
owns:                          # globs of code this doc describes (repo-relative)
  - backend/src/**/FlexiAnalytics/**
verified_at: 1d75813bb         # commit the doc was last checked against
related:                       # other process names, upstream or downstream
  - feed-metabase-views
---
```

**Body template** (fixed section order, all headings required; "None" is a valid body):

1. **Purpose** — which business question this process answers.
2. **Trigger** — Hangfire job id, cron (Europe/Prague), manual trigger, or on-demand.
3. **Data flow** — source (system / endpoint / table) → steps → target (table / cache / view).
4. **Logic & formulas** — exact rules, units, with/without VAT, time windows.
5. **Configuration** — setting keys and repo defaults.
6. **Runtime facts** — prod values and data observations, each with source and date
   (e.g. "`ManufactureCostHistoryDays` = 730, no prod override — KV checked 2026-09-20").
7. **Known quirks** — gotchas, migrated from agent memory.
8. **Code entry points** — the files to open first.

The template lives at `docs/processes/_TEMPLATE.md`.

**Index:** `docs/processes/INDEX.md` is **generated** (never hand-edited) from frontmatter —
grouped by kind, one line per process: name, summary, related. Agents read it first and open
only the doc they need.

**Language:** English. Colleagues may ask in Czech; Claude translates.

## 2. Serving

**Bundling:** `docs/processes/*.md` are included as embedded resources in the API assembly via
a csproj glob. No Dockerfile change. Every deployed image carries the docs matching its own code,
so production answers describe production code and staging answers describe staging.

**MCP tools** — new `ProcessDocsMcpTools` in `backend/src/Anela.Heblo.API/MCP/Tools/`, following
the `KnowledgeBaseTools` pattern (MediatR use cases in Application, thin tool class):

- `ListProcesses(kind?)` — returns the index entries (name, kind, summary, related).
- `GetProcessDoc(name)` — returns the full markdown and `verified_at`. Unknown name → `McpException`
  listing close matches.

Tool descriptions instruct Claude to: call `ListProcesses` first; follow `related` links for
upstream questions; cite the doc name and `verified_at` in the answer; say so when a doc's
runtime facts are old rather than presenting them as current.

The resource reader is a small `IProcessDocStore` (Application) with an embedded-resource
implementation, parsing frontmatter once at startup.

**Permission:** new feature `ProcessDocs` (read) added through the access-matrix source that
generates `Feature.generated.cs` (AccessMatrixGen) — not by editing the generated file. Granted to
roles chosen during implementation (default: everyone who has MCP access).

**Dev side:** a small repo skill `/process` (`.claude/skills/process/SKILL.md`) that answers the
same questions by reading `docs/processes/` directly, and points to code entry points when the
developer wants to go deeper.

**Docs:** `docs/integrations/mcp-server.md` updated with the two tools (tool count 28 → 30).

## 3. Freshness

### 3.1 Staleness script — `scripts/process-docs/check.py`

Deterministic, no LLM, runs in seconds. Reports:

- **Stale** — a doc where any file matching `owns` changed between `verified_at` and the compared ref.
- **Orphan** — a process-bearing class not covered by any doc's `owns`. Detection starts with every
  implementation of `IRecurringJob`, plus an explicit include/ignore list in
  `scripts/process-docs/config.yaml` (calculations have no marker type today).
- **Dead glob** — an `owns` pattern that matches no files.
- **Schema errors** — missing frontmatter fields, unknown `kind`, unknown `related` name,
  missing template headings.
- **Index drift** — `INDEX.md` differs from what the script would generate.

Modes: `check` (report, exit code), `index` (regenerate `INDEX.md`), `--json` for the routine.

Tests: pytest over fixture repos (temp git repo with commits) covering each report type.

### 3.2 PR CI job

New job in the existing PR workflow, running `check.py` against the PR's base:

| Finding | Effect |
|---|---|
| PR touches owned code, doc not updated and `verified_at` not bumped | PR comment (warn) naming the doc: "update it, or bump `verified_at` if behaviour didn't change" |
| Orphan | **warn** during backfill, switched to **fail** once backfill is complete |
| Schema error, dead glob, index drift | fail |

The pipeline skills (`implement-next-task`, `automerge-pr`, `rework-pr`) are told — via CLAUDE.md —
to treat the stale-doc comment as part of the change: update the doc in the same PR.

### 3.3 CLAUDE.md rule

Add to *Project-specific rules*: a PR that adds or changes a sync, calculation or feed updates or
creates its `docs/processes/` doc in the same PR; runtime facts discovered while debugging go to
the doc's *Runtime facts* / *Known quirks* rather than only to agent memory. Add
`docs/processes/INDEX.md` to the documentation map.

### 3.4 Weekly routine — `docs/routines/process-docs-refresh.md`

Remote Claude Code routine (same setup as `daily-arch-review`), weekly. Each run:

1. Runs `check.py --json` on `main`.
2. **Stale set:** for each stale doc, reads the doc plus `git diff <verified_at>..HEAD -- <owns>`;
   bumps `verified_at` if behaviour is unchanged, otherwise edits the doc.
3. **Rotating deep audit:** re-verifies the 5 docs with the oldest `verified_at` against current
   code, even if not stale — catches errors from the original drafting.
4. **Orphans:** drafts a doc for each.
5. **Runtime facts:** does not verify them; flags facts older than 90 days in the PR body.
6. Opens **one PR** with all changes (not a direct commit to main), reviewed like any other —
   eligible for `/automerge-pr`.

If nothing is stale, no orphans exist and the audit finds nothing, it opens no PR.

## 4. Rollout

**PR 1 — infrastructure + exemplars:**
template, `check.py` + tests + config, CI job (orphans in warn mode), `IProcessDocStore` +
use cases + `ProcessDocsMcpTools` + tests, `ProcessDocs` permission, `/process` skill,
CLAUDE.md rule, mcp-server.md update, generated `INDEX.md`, and three exemplar docs:

- `calc-margins.md` (M0–M3)
- `sync-flexi-analytics.md`
- `calc-stock-up.md`

The developer reviews the exemplars to lock the format before the backfill.

**PRs 2..n — backfill by domain:** Flexi, Shoptet, marketing, margins & finance,
stock & manufacture, misc. Each batch is agent-drafted from code, seeded with the relevant
agent-memory gotchas; migrated gotchas are trimmed from memory afterwards.

**Final PR:** switch orphan check to fail; create the weekly routine.

## Error handling

- Missing or malformed doc at startup: the store logs an error and skips that doc; other docs
  still serve. CI prevents this reaching `main`.
- `GetProcessDoc` with unknown name: `McpException` with close matches (not a 500).
- Permission missing: `EnsureFeatureAccess` → standard MCP authorization error.
- `check.py` on a doc whose `verified_at` is not in history (e.g. after squash): treated as
  stale, reported explicitly as "unknown commit".

## Testing

- **Backend:** unit tests for frontmatter parsing, index building, `ListProcesses` kind filter,
  `GetProcessDoc` found / not found; a test asserting every embedded doc parses (so a broken doc
  fails `dotnet test`, not just CI's script).
- **Script:** pytest with temporary git repos for stale / orphan / dead glob / schema / index drift.
- **Manual:** ask the three exemplar questions from claude.ai against staging after deploy.

## Open decisions (resolved during implementation)

- Which roles get `ProcessDocs` (default: all MCP users).
- Exact include/ignore list for calculation classes in `config.yaml`.
