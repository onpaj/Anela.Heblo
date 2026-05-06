# Architecture Review: Parallelize Invoice Fetch in `ClassifyInvoices` Handler

## Architectural Fit Assessment

The change is tightly localized to a single MediatR handler in a vertical-slice module (`Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/`) and aligns cleanly with existing project patterns:

- **No cross-module impact.** Public DTOs (`ClassifyInvoicesRequest`/`Response`), the domain abstraction `IReceivedInvoicesClient`, the controller, and the `InvoiceClassificationJob` caller are all unchanged. No OpenAPI client regeneration required.
- **Bounded-concurrency pattern is already established.** The codebase uses both `Parallel.ForEachAsync` (`DashboardService.cs:131` with `MaxDegreeOfParallelism = MaxConcurrentTileLoads`) and `SemaphoreSlim`-based throttles (`SalesCostProvider`, `ProductEnrichmentCache`, `DirectManufactureCostProvider`). Either pattern fits.
- **DI lifetime is compatible.** `FlexiReceivedInvoicesClient` is registered `Scoped` (`FlexiAdapterServiceCollectionExtensions.cs:85`); a single MediatR request shares one scope, so concurrent invocation occurs against one instance — its dependencies (`IReceivedInvoiceClient` from the Flexi SDK, `IOptions<>`, `TimeProvider`, `IMapper`, `ILogger`) are all stateless or thread-safe per call. **Verify `Rem.FlexiBeeSDK` `IReceivedInvoiceClient.GetAsync` is reentrant** — that is the only unverified node in the concurrency graph.
- **The single-handler cohesion principle is preserved.** No new service class is needed; the change stays inside the slice.

## Proposed Architecture

### Component Overview

```
Controller / InvoiceClassificationJob
        │
        ▼
ClassifyInvoicesHandler.Handle(...)
        │
        ├── if (InvoiceIds non-empty)
        │       └── [NEW] ParallelFetch ─── throttle(SemaphoreSlim, max=8)
        │                  │
        │                  ▼
        │           IReceivedInvoicesClient.GetInvoiceByIdAsync (xN, in flight)
        │                  │
        │                  ▼
        │       results in input order ─► invoicesToClassify + errorMessages
        │
        └── else
                └── IReceivedInvoicesClient.GetUnclassifiedInvoicesAsync()  [unchanged]

Then (unchanged, sequential):
        invoicesToClassify ──► IInvoiceClassificationService.ClassifyInvoiceAsync (per item)
```

### Key Design Decisions

#### Decision 1: Parallel mechanism — `Task.WhenAll` + `SemaphoreSlim` (preferred), not `Parallel.ForEachAsync`
**Options considered:**
- A. `Task.WhenAll` over `Select(async id => …)` with a `SemaphoreSlim` throttle (spec's choice).
- B. `Parallel.ForEachAsync` with `ParallelOptions.MaxDegreeOfParallelism` (pattern used in `DashboardService`).

**Chosen approach:** Option A.

**Rationale:**
- FR-5 requires fetch errors to be reported **in input order**. `Task.WhenAll` naturally returns results in input order (one task per id, materialized into a tuple). With `Parallel.ForEachAsync` you would have to capture an index into a `ConcurrentBag`/array and reorder, adding code without buying anything.
- Cancellation semantics for both are equivalent at the throttle boundary (NFR-2): cancelling on `SemaphoreSlim.WaitAsync(ct)` short-circuits unstarted work; in-flight Flexi calls are not cancellable today regardless of mechanism.
- The throttle pattern is already used elsewhere in the codebase, so it does not introduce new vocabulary.

#### Decision 2: Concurrency limit lives as a private constant, not a config option
**Options considered:**
- A. `private const int MaxFetchConcurrency = 8;` inside the handler (spec's choice).
- B. Add `InvoiceClassificationOptions.MaxFetchConcurrency` to `IOptions<>` infrastructure.

**Chosen approach:** Option A, with a clear path to B if Flexi load issues appear.

**Rationale:** YAGNI. There is no current evidence that the value needs per-environment tuning, and the surrounding `DataSourceOptions` already exists if it ever does. Promotion is a one-line change and not a public-API change.

#### Decision 3: Per-id exception handling is added now (not a pure refactor)
**Options considered:**
- A. Pure parallel translation: a single Flexi exception fails the entire batch (matches today's behavior at line 42).
- B. Catch fetch exceptions per id, increment `Errors`, append a descriptive message, log, and continue with the rest (FR-4).

**Chosen approach:** Option B.

**Rationale:** With sequential code, an exception "stop everything" was at least debuggable because you knew which id failed. With parallel code, an unhandled exception inside `Task.WhenAll` gives an `AggregateException` and may abandon already-fetched invoices' buffers — worse UX. The spec's call to tighten this is correct and the extra cost is one `try/catch` block. Document the behavior change in the PR description so the reviewer is not surprised.

#### Decision 4: Result reassembly via materialized tuple array
**Options considered:**
- A. Each task returns `(Id, Invoice?, Error?)`; aggregate after `Task.WhenAll` in input order.
- B. Each task pushes directly to shared `List<>` collections under a lock.

**Chosen approach:** Option A.

**Rationale:** Immutable per-task outputs + a single sequential aggregation pass eliminates the need for any synchronized collection — the only shared state is the `SemaphoreSlim`. This matches the codebase's preference for immutable patterns and removes a class of concurrency bugs by construction.

## Implementation Guidance

### Directory / Module Structure
No new files. Edit only:
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`

New tests under:
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs` (new file, mirrors `ClassificationHistoryRepositoryTests.cs` location and xUnit style).

### Interfaces and Contracts
Unchanged:
- `IReceivedInvoicesClient` — keep as-is. Do **not** add a `CancellationToken` overload in this change (out of scope).
- `ClassifyInvoicesRequest` / `ClassifyInvoicesResponse` — unchanged.
- `IInvoiceClassificationService` — unchanged; classification loop remains sequential.

Internal contract (private to handler):
```csharp
private const int MaxFetchConcurrency = 8;

// Per-id outcome — immutable record local to the handler.
private readonly record struct FetchOutcome(string Id, ReceivedInvoiceDto? Invoice, string? FetchError);
```

Place `FetchOutcome` either as a nested type or as a `file`-scoped type in the same file; keep it private to preserve the slice boundary.

### Data Flow

For specific-invoices mode with `InvoiceIds = [A, B, C]`:

1. Allocate `SemaphoreSlim(MaxFetchConcurrency, MaxFetchConcurrency)` (using-scoped).
2. Map ids to tasks: each task awaits `throttle.WaitAsync(ct)` → calls `_invoicesClient.GetInvoiceByIdAsync(id)` inside a `try/catch` → releases in `finally` → returns a `FetchOutcome`.
3. `await Task.WhenAll(tasks)` — gives an array in input order.
4. Stop stopwatch, emit one debug log: `"Fetched {FetchedCount}/{RequestedCount} invoices in {ElapsedMs}ms"`.
5. Sequentially walk the result array:
   - `Invoice != null` → append to `invoicesToClassify`.
   - `Invoice == null && FetchError != null` → `response.Errors++`, `errorMessages.Add($"Invoice {id}: fetch failed: {error}")`, `_logger.LogError`.
   - `Invoice == null && FetchError == null` → not-found path: `response.Errors++`, `errorMessages.Add($"Invoice {id} not found")`, `_logger.LogWarning`.
6. Continue with the unchanged classification loop.

Ordering invariant (FR-5): all entries appended to `errorMessages` during step 5 precede entries appended during the classification loop, because steps run strictly in sequence.

### Testing strategy

Use the project's xUnit + FluentAssertions stack with hand-rolled fakes (consistent with `ClassificationHistoryRepositoryTests`).

| Test | Verifies |
|------|----------|
| `Handle_FetchesInParallel_WhenMultipleIds` | 10 ids × 200 ms fake delay completes < 800 ms (FR-1 / NFR-1). |
| `Handle_RespectsConcurrencyLimit` | A counting fake (atomic in-flight counter) never exceeds `MaxFetchConcurrency` (FR-2). |
| `Handle_AppendsNotFoundError_AndContinues` | One id returns `null`; response has correct `Errors`, `ErrorMessages`, and the rest classify (FR-3). |
| `Handle_FetchExceptionIsolatedToOneId` | One id throws; others succeed; `Errors` incremented; descriptive error in `ErrorMessages`; warning log emitted (FR-4). |
| `Handle_PreservesInputOrderOfErrors` | Mixed missing/throwing ids; `ErrorMessages` order matches `InvoiceIds` order (FR-5). |
| `Handle_BatchModeUnchanged` | `InvoiceIds == null` path still calls `GetUnclassifiedInvoicesAsync` exactly once (FR-6). |

Use `Mock<IReceivedInvoicesClient>` (Moq) or a tiny fake class with an `int Interlocked` in-flight counter for the concurrency-limit test.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Underlying `Rem.FlexiBeeSDK.IReceivedInvoiceClient.GetAsync` carries non-thread-safe state (e.g., shared mutable HttpRequestMessage). | High if true | Run the in-process concurrency test against the real adapter once before merge; if it fails, fall back to a wider `SemaphoreSlim(1)` per-instance lock at the adapter level (separate change) and keep concurrency at the handler — but this would erase the wins, so verify early. |
| Flexi server rate-limits at >8 concurrent reads from one client and starts returning 429s. | Medium | Conservative default of 8; new debug log makes detection trivial in App Insights. Mitigation if it occurs: lower the constant, then promote to options. |
| Behavior change: today a single Flexi fetch exception aborts the request; after this, it is reported per-id and the rest succeed. | Low | Document explicitly in PR description and release notes. Existing callers (controller, job) inspect `response.Errors`/`ErrorMessages`, so no caller code breaks. |
| Cancellation tokens flow only as far as `throttle.WaitAsync(ct)` — in-flight Flexi calls cannot be cancelled. | Low | Acknowledged in spec; surface in code comment near `WaitAsync` and in PR description. Adding `CancellationToken` to the Flexi adapter is a separate work item. |
| `Task.WhenAll` materializes one `Task` per id; for very large `InvoiceIds` (e.g., 10k) this allocates heavily. | Low | Realistic input sizes are 1–100 (manual UI trigger) and a few hundred at most for the job. Document the implicit cap in code comment; revisit if the use case grows. |
| App Insights regression invisible because P50 metric not emitted. | Low | The new debug log (`"Fetched {FetchedCount}/{RequestedCount} in {ElapsedMs}ms"`) gives a structured property that is queryable. No telemetry-pipeline change required. |

## Specification Amendments

The spec is implementation-ready. Two small clarifications to add before coding:

1. **FetchOutcome shape.** The spec sketch uses C# tuples. Recommend a `private readonly record struct FetchOutcome(string Id, ReceivedInvoiceDto? Invoice, string? FetchError)` for self-documenting field names. Tuples are fine but record-struct is cleaner for the test assertions.
2. **Logging level for fetch exceptions.** The spec calls for `_logger.LogError` (correct), and for not-found it preserves `_logger.LogWarning`. Make explicit in the spec that the new debug-level summary log fires **always** (even on failures and zero ids) so dashboards can chart fetch latency consistently.
3. **Input-order traversal note.** Make explicit in the spec that the post-`WhenAll` aggregation must traverse `fetchResults` in array order (which is `request.InvoiceIds` order); the sketch already does this but the FR-5 invariant deserves a one-line callout in code as a comment to prevent a future refactor from reordering for "efficiency".

No additional behavior beyond what the spec already lists.

## Prerequisites

None. No migrations, no configuration keys, no infrastructure, no DI changes. The work can start immediately.

Recommended one-time pre-flight (5 min): manually spike a 10-id call against the staging Flexi instance from a scratch test to confirm `Rem.FlexiBeeSDK.IReceivedInvoiceClient.GetAsync` tolerates concurrent invocation. If it does not, this becomes a two-change effort and the adapter must be made reentrant first.