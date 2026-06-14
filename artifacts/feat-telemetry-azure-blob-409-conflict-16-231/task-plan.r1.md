Plan saved to `artifacts/feat-telemetry-azure-blob-409-conflict-16-231/plan.r1.md`.

**Summary of the plan:**

8 tasks split across the diagnostic and remediation phases:

1. **Task 1 — Phase 1 diagnostic**: KQL query + populated `docs/features/azure-blob-409-diagnostic.md` confirming `BlobContainerClient.CreateIfNotExistsAsync` as the 409 source.
2. **Task 2 — `BlobContainerEnsurance` helper (TDD)**: 6 unit tests covering cache-hit, missing-container, 4-way parallel race, benign 409 race, and non-409 failure → cache eviction.
3. **Task 3 — Wire helper into `AzureBlobStorageService`**: replace `ConcurrentDictionary<string, bool>` with `ConcurrentDictionary<string, Lazy<Task>>`, update existing tests' `CreateIfNotExistsAsync` expectations to `ExistsAsync`-first.
4. **Task 4 — Wire helper into `AzureBlobPrintQueueSink`**: drop the `SemaphoreSlim`/`bool` gate, route through helper, update gate-behaviour tests.
5. **Task 5 — `BlobIdempotent409TelemetryProcessor` backstop (TDD)**: 5 unit tests asserting only `PUT container` 409s are re-marked, blob-path 409s pass through.
6. **Task 6 — Register processor + update observability docs**: order matters (backstop before `CostOptimizedTelemetryProcessor`).
7. **Task 7 — FR-3 verification queries**: 7-day post-deployment KQL for failure rate (≤0.5%) and latency (p50/p95/p99 thresholds).
8. **Task 8 — Final validation**: full build, full test run, grep for stray `CreateIfNotExistsAsync` call sites.

Every task is TDD-ordered (failing test → implementation → passing test → format → commit) with full code blocks inline. The acceptance summary table at the end maps each spec FR/NFR to its implementing task.