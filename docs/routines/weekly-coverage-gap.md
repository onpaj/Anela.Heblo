# Weekly Coverage Gap Routine

## Overview

A remote Claude Code routine that runs every Monday, downloads coverage artifacts from the latest successful CI run, and files GitHub issues only for files where untested code contains real business logic. It is not a coverage enforcement tool — it is a signal for where meaningful verification is missing.

## Routine details

| Field | Value |
|---|---|
| Routine ID | `trig_01Pqv2XtmBPGpcfaycWUj2Ns` |
| Schedule | Weekly, every Monday (`0 4 * * 1` UTC = 6am Europe/Prague CEST) |
| Model | `claude-sonnet-4-6` |
| Repo | `https://github.com/onpaj/Anela.Heblo` |
| Web UI | https://claude.ai/code/routines/trig_01Pqv2XtmBPGpcfaycWUj2Ns |

## How it works

1. Downloads `coverage-backend` (latest successful nightly run of `e2e-nightly-regression.yml`) and `coverage-frontend` (latest successful main-branch CI run) artifacts
2. Parses Cobertura XML (backend) and LCOV (frontend) to identify files below 60% line coverage
3. Reads each low-coverage file and qualifies whether the gap is meaningful
4. Files GitHub issues only for real gaps — skips boilerplate and trivial code

## What it skips (do not file)

- Controllers that only call `_mediator.Send()`
- DTO / Request / Response classes with no behaviour
- Startup, DI registration, configuration, migrations
- Pure presentational React components
- Auto-generated OpenAPI clients

## What it flags (file an issue)

- MediatR handlers with multiple conditional branches or validation logic
- Domain entities/services with business rules, state machines, calculations
- Financial logic: margins, pricing, totals (FinancialOverview, Invoices, Bank, Purchase)
- Stock/inventory logic with quantity checks or status transitions (Logistics, Manufacture)
- Error/exception paths where the failure shape is never tested
- React hooks/components with significant state logic or side effects

## Output

Issues are labelled `coverage-gap` + `tech-debt`. Each issue includes the specific file, exact untested logic, impact if it regresses, and a suggested test approach. Target is 0–5 issues per run; zero is a valid outcome.

Find all open coverage gap issues:
```
https://github.com/onpaj/Anela.Heblo/issues?q=label%3Acoverage-gap+is%3Aopen
```

## CI dependency

The routine depends on two artifacts:

| Artifact | Path | Retention | Produced by |
|---|---|---|---|
| `coverage-backend` | `coverage/**/*.cobertura.xml` | 7 days | `e2e-nightly-regression.yml` → `backend-coverage` job |
| `coverage-frontend` | `frontend/coverage/lcov.info` | 7 days | `ci-main-branch.yml` → `frontend-tests` job |

Backend coverage is nightly-only: the XPlat coverage collector adds ~10 min to the backend test run (it instruments every referenced assembly per test project), so it was removed from the PR and main CI. The Codecov backend upload happens in the same nightly job.

## Managing the routine

**Pause/enable/delete:** https://claude.ai/code/routines/trig_01Pqv2XtmBPGpcfaycWUj2Ns
