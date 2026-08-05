# Merge review — PR #3779 (Npgsql `TaskCanceledException@NpgsqlWriteBuffer.Flush` resilience fix)

**Verdict: REJECT** (leave for a human glance). Not because the change is broken — it is competent and mostly safe — but because one bundled element (a global per-attempt DB timeout cut from 10s → 3s applied to a pipeline that also wraps 500-row batch analytics writes) is an unverified production-regression risk I cannot rule out by reading.

## What the PR does (matches the issue #3758)
- **Core fix (FR-1):** `TransientErrorClassifier` now treats `Polly.Timeout.TimeoutRejectedException` as transient, so per-attempt DB timeouts are retried instead of surfacing as `TaskCanceledException`. Sound, and applied at pipeline level so it covers `FeatureFlags/Get` as the issue asked.
- **Coupled config (FR-2):** `Database:Resilience:TotalTimeBudget` `00:00:10` → `00:00:03` in all three `appsettings*.json`. In the current `AddRetry(...).AddTimeout(...)` composition this is a **per-attempt** ceiling; worst case becomes ~13.4s (4 attempts).
- **Observability (FR-3):** exception `.Data["Anela.DbRetryAttempts"]` tagging in `PollyExecutionStrategy`; `HebloFeatureProvider` logs `dbRetryExhausted`/`attempts`; `NpgsqlConnectionInterceptor` records pool-wait on the failure path too. All additive, no contract change.
- **Docs + tests:** dated KQL correlation note; 6 test files incl. new `NpgsqlConnectionInterceptorTests.cs`.
- **Scope discipline:** FR-4 (pool-size tuning) deliberately not touched, evidence-gated. Good.

## Independent verification I ran
- `dotnet build` of `Anela.Heblo.Persistence` (pulls Domain/Application transitively): **0 errors**, 88 pre-existing nullable warnings.
- `dotnet test` filtered to `~Resilience | ~HebloFeatureProvider`: **69/70 passed**. The one failure, `Pipeline_AbortsByTotalTimeBudget`, is a **pre-existing timing flake** — I confirmed via `git diff origin/main...HEAD` that the test body is unmodified (it appears only as a context line in the diff); it runs a 50-attempt loop asserting a hardcoded `< 5s` wall-clock bound, which the loaded sandbox exceeds. Unrelated to this change (SocketException was already transient).
- Confirmed the two known-slow query paths (`LeafletDocumentRepository.SearchSimilarAsync`, `KnowledgeBaseRepository`, both `CommandTimeout=120` pgvector searches) use **raw `NpgsqlCommand`**, bypassing the EF execution strategy — so they are **not** affected by the 3s cap. Good sign the team routes slow work around the pipeline.
- Confirmed the new `Pipeline_DoesNotRetry_OnAmbientCancellation` test: caller-token cancellation is not retried, only Polly's own per-attempt timeout — this correctly prevents retry amplification when the *caller* has given up.

## The blocking risk — global 3s timeout on a shared pipeline that wraps batch analytics writes
Both `ApplicationDbContext` (`PersistenceModule.cs`) **and** `AnalyticsDbContext` (`AnalyticsPersistenceModule.cs`) inject the **same singleton** `IDbResiliencePipelineProvider`, built from `Database:Resilience` (the section this PR retunes to 3s). Any EF-mediated query or `SaveChanges` on either context is now cut off at 3s per attempt and, on timeout, retried up to 4×.

Concrete exposure I traced: `Adapters.Flexi/Analytics/LedgerSyncService.UpsertBatchAsync` (and sibling sync services) do a read + a single `SaveChangesAsync` upserting up to **500 rows** (`FlexiAnalyticsSync:BatchSize = 500`) through `AnalyticsDbContext`. A 500-row upsert against Azure Postgres Flexible Server can plausibly exceed 3s. Before this PR it had a 10s per-attempt budget and completed in one attempt; after, it is cancelled at 3s, classified transient, and re-run 3 more times (each again cancelled at 3s) → the batch **fails after ~13s** instead of succeeding.

This is *not* protected by the ambient-cancellation guard: the sync caller tolerates 120s (`RequestTimeoutSeconds: 120`, analytics `ConnectionLifetime=600`), so it never cancels — it is Polly's own new 3s timeout that kills and retries it. Under DB load this also amplifies write pressure (repeated cancelled/rolled-back 500-row upserts) — the opposite of the intent, on the exact pool-contention hot path the issue is about.

The plan/design/architecture/review reasoned the 3s cut only against request-serving paths ("browser fetch, health probe, generous for a normal query"). None of the four artifacts analyzed the shared pipeline against the analytics **batch-write** path. I cannot confirm from reading whether a production 500-row `LedgerEntry`/`Contact`/`Ledger` upsert stays under 3s — and if it doesn't, this silently breaks the nightly Flexi analytics sync.

## Why reject rather than approve
- Cross-cutting production **resilience/timeout** change affecting every EF DB operation — explicit blast-radius territory.
- The one risky element is justified by reasoning, not measurement, and has a **plausible, concrete regression** (analytics sync batches >3s) that reading cannot rule out.
- On a path where **five prior "completed" fixes did not hold** — arguing for caution, not confidence, on further global changes here.
- A human who knows the production `SaveChanges` latency profile clears this in one glance; a wrong merge risks the nightly sync and adds retry load under contention. The cost asymmetry favors a human look.

If the timeout reduction were dropped (keeping only the classifier + observability changes) or scoped so analytics batch writes kept a longer budget, I would approve.

```json
{"outcome": "reject", "summary": "Core classifier fix + observability changes are sound (build clean, 69/70 targeted tests pass, lone failure is a pre-existing unmodified timing flake). But the bundled global per-attempt DB timeout cut 10s->3s applies to the shared resilience pipeline that also wraps AnalyticsDbContext 500-row batch upserts (LedgerSyncService.UpsertBatchAsync via nightly Flexi sync, caller tolerates 120s so the ambient-cancel guard doesn't protect it); a batch legitimately exceeding 3s now times out and retries into failure. Unverified production-regression risk on a hot path with five prior failed fixes — leave for a human glance.", "confidence": 0.34}
```
