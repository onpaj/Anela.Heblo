# Merge review — PR #3846

**Title:** [arch-review] TransportBoxes: Receive creates & commits stock-up operations in a separate transaction from the box state change (non-atomic, unrecoverable on failure)
**Base:** `main` ← **Head:** `harness/tsk_18441b978cc7455a`
**Closes:** #3844

## Verdict: REJECT

The intended bug fix is sound, but the PR's diff to `main` is contaminated with a large,
unrelated body of work that is neither described in the PR nor reviewed by this task. Merging
it unattended would land ~4,000 lines of undescribed CI tooling onto the default branch.

## Scope contamination (dispositive)

`git diff --stat origin/main...HEAD` = **123 files, +5,396 / −15**. Breakdown:

| Group | Files | Relation to PR |
|---|---|---|
| Atomicity fix (C# src + tests) | 9 | The actual subject of #3844 |
| `.artifacts/…` (this task's step outputs) | 5 | Harness bookkeeping |
| `docs/routines/test-health/**` | **109** | **Unrelated** — a ReportPortal/GitHub "test-health" digest routine |

The 109 unrelated files are a self-contained CI/observability routine: `test-health-digest.sh`
(627 lines), `gh-api.sh`, `rp-query.sh`, a harness installer (`harness/install.sh`,
`test-health.agent.json`, `test-health.process.json`), a design spec + 1,594-line implementation
plan, and ~100 fixture JSONs replaying GitHub Actions / ReportPortal API responses. `git log`
shows ~40 `feat/fix(test-health)` commits sitting *beneath* the six atomicity commits — the branch
was cut from another task's branch instead of `main`, so all of test-health rides along.

The PR body describes **only** the atomicity fix and closes **only** #3844. The test-health work
is invisible to anyone reading the PR, was never covered by this task's plan/design/architecture/
review artifacts, and includes shell scripts that execute external API calls and install harness
routines (real blast radius). This is exactly the "unrelated scope → withhold" case: I cannot
approve a merge where 109 of 123 files are outside the stated change and unreviewed here.

## The intended fix (the 9 in-scope files) — looks correct

- `ChangeTransportBoxStateHandler.HandleReceived` swaps `CreateOperationAsync` (which saved the
  stock-up op in its own `SaveChangesAsync`, separate from the box-state save) for a new
  `StageOperationAsync` that adds the operation to the tracker **without** saving, so it commits
  atomically with the box state in a single `SaveChangesAsync`.
- `StageOperationAsync` is idempotent: it short-circuits when a row with the same `DocumentNumber`
  already exists (via existing `GetByDocumentNumberAsync`), making retries safe.
- Interface/adapter plumbing (`IStockUpProcessingService`, `StockUpProcessingService`,
  `ILogisticsStockOperationService`, `LogisticsStockOperationAdapter`) is consistent and mirrors
  the existing `CreateOperationAsync` path.
- Matches design-01.md / architecture-01.md exactly, incl. the required test correction (inject a
  non-transient failure so the Polly execution strategy can't retry it into a false pass). A real-
  Postgres integration test covering atomicity-on-failure and idempotent-retry is included.

If this fix were presented alone against `main`, it would be a strong approve.

## Why reject rather than approve

1. **Unreviewable/undescribed scope** — 109 unrelated files, none mentioned in the PR body,
   none covered by the task's review chain. Merging brings undocumented CI tooling into `main`.
2. **Blast radius in the unrelated work** — the test-health scripts run GitHub/ReportPortal API
   calls and install harness routines; that deserves its own reviewed PR, not a silent ride-along.
3. **Wrong base** — the branch history shows it was branched off the test-health task, not `main`;
   the correct remedy is to rebase the atomicity fix onto `main` (or open a clean PR) so the diff
   contains only the 9 intended files.

A rejection here costs a human one glance; approving would merge 4,000 unreviewed lines under the
banner of a bug fix.

```json
{"confidence": 0.04, "reasoning": "The intended atomicity fix (9 files) is correct and well-scoped, but the PR's diff to main includes 109 unrelated files (~4,000 lines) of a 'test-health' CI routine — shell scripts hitting external APIs, a harness installer, fixtures — that are undescribed in the PR body and unreviewed by this task; the branch was cut from the wrong base.", "risks": ["Merges an entire unrelated test-health routine (109 files) into main under a bugfix PR", "Unreviewed shell scripts execute GitHub/ReportPortal API calls and install harness routines", "PR body and closed issue #3844 do not mention the bundled work, so future readers won't know it landed"]}
```
