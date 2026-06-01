# Daily Architecture Review Routine

## Overview

A remote Claude Code routine that reviews one backend module per day and files GitHub issues for any real architecture violations or refactoring opportunities found. Cycles through all 29 modules roughly every month.

## Routine details

| Field | Value |
|---|---|
| Routine ID | `trig_01TDp4EDif36TJBkrMR2R7v2` |
| Schedule | Daily, every day (`0 23 * * *` UTC = 1am Europe/Prague CEST) |
| Model | `claude-sonnet-4-6` |
| Repo | `https://github.com/onpaj/Anela.Heblo` |
| Environment | Anthropic Cloud (`env_01Ggx2T42z3VtZqdp8k7TSTW`) |
| Web UI | https://claude.ai/code/routines/trig_01TDp4EDif36TJBkrMR2R7v2 |

## Module rotation

The routine selects today's module deterministically: `modules[(dayOfYear - 1) % 29]`

Modules in rotation order (index 0–28):

| # | Module | # | Module |
|---|---|---|---|
| 0 | Analytics | 15 | Journal |
| 1 | Article | 16 | KnowledgeBase |
| 2 | BackgroundJobs | 17 | Leaflet |
| 3 | Bank | 18 | Logistics |
| 4 | Catalog | 19 | Manufacture |
| 5 | Configuration | 20 | Marketing |
| 6 | Dashboard | 21 | MarketingInvoices |
| 7 | DataQuality | 22 | OrgChart |
| 8 | ExpeditionList | 23 | PackingMaterials |
| 9 | ExpeditionListArchive | 24 | Photobank |
| 10 | FileStorage | 25 | Purchase |
| 11 | FinancialOverview | 26 | ShoptetOrders |
| 12 | GridLayouts | 27 | UserManagement |
| 13 | InvoiceClassification | 28 | Users |
| 14 | Invoices | | |

## What it reviews

For each module the routine reads the architecture docs first, then inspects backend (Domain / Application / API / Persistence layers) and frontend (pages, components, hooks). It checks for:

- **Clean Architecture violations** — wrong layer dependencies, business logic in controllers, DTOs outside `contracts/`, cross-module entity access
- **Project-specific rules** — DTOs must be classes not records; API hooks must use absolute URLs
- **SOLID violations** — SRP, OCP, LSP, ISP, DIP
- **KISS / YAGNI** — dead code, speculative abstractions, unnecessary indirection
- **Refactoring opportunities** — real duplication, methods > 50 lines, files > 800 lines
- **Frontend** — components mixing concerns, missing error/loading states

## Output

Each run files 0–5 GitHub issues labelled `arch-review` plus a secondary label (`tech-debt`, `refactoring`, `code-quality`, `architecture`, `complexity`, etc.). Issues include the file path, line range, impact, and a concrete minimal fix suggestion.

The routine never makes code changes, never opens PRs, never commits.

## Managing the routine

**Pause/enable/delete:** https://claude.ai/code/routines/trig_01TDp4EDif36TJBkrMR2R7v2

**Trigger a manual run:**
```bash
# Ask Claude Code to run it, or use the web UI
```

**Update the prompt** (e.g. to tune quality bar or add modules): ask Claude Code — `action: update` on the routine ID above.

## Triage

Issues filed by this routine appear at:
```
https://github.com/onpaj/Anela.Heblo/issues?q=label%3Aarch-review+is%3Aopen
```

Aim to review and close/resolve them periodically. Issues with no activity after ~90 days are candidates for closing as "won't fix" or "stale".
