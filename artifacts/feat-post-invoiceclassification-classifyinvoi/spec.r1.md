# Specification: Parallelize Invoice Fetch in `ClassifyInvoices` Handler

## Summary

`POST /api/InvoiceClassification/classify` currently averages ~15 s when callers pass `InvoiceIds` because the handler fetches each invoice sequentially via `IReceivedInvoicesClient.GetInvoiceByIdAsync`. This change replaces the sequential `foreach` fetch with parallelized fetches (bounded concurrency) so total latency scales with the slowest call rather than the sum, while preserving existing per-invoice error reporting and downstream classification semantics.

## Background

`ClassifyInvoicesHandler` (`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs:36-55`) handles two modes:

1. **Specific-invoices mode** — when `request.InvoiceIds` is non-empty, each id is fetched individually from Flexi via `_invoicesClient.GetInvoiceByIdAsync(invoiceId)` inside a `foreach` loop.
2. **Batch mode** — when `request.InvoiceIds` is null/empty, all unclassified invoices are fetched in a single `GetUnclassifiedInvoicesAsync` call.

Mode 1 is the slow path. Each Flexi REST call costs hundreds of milliseconds; with 20–50 ids the total round-trip dominates the response (~15 s observed in App Insights). Because each call is independent, sequential awaiting wastes nearly all wall-clock time.

The downstream `_classificationService.ClassifyInvoiceAsync` step (`ClassifyInvoicesHandler.cs:65-105`) already runs after fetching is complete and is **out of scope** — it has its own ordering/state requirements (rule lookup, history writes, classification client calls) and must remain sequential in this change.

The Flexi SDK (`Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices.IReceivedInvoiceClient`) exposes only `GetAsync(id)` and `SearchAsync(ReceivedInvoiceRequest)` — there is no bulk-by-ids endpoint at the SDK level (verified: only one usage of `SearchAsync`/`GetAsync` for received invoices in `FlexiReceivedInvoicesClient.cs:37,45`). Adding a bulk fetch would require either Flexi-side filter chaining or N concurrent calls under the hood, so we adopt option 1 from the brief: parallel fetch in the handler.

## Functional Requirements

### FR-1: Parallelize specific-invoice fetch

When `request.InvoiceIds` contains one or more ids, the handler must issue the per-id `GetInvoiceByIdAsync` calls concurrently rather than sequentially.

**Acceptance criteria:**
- Given N invoice ids, the wall-clock time of the fetch phase is bounded by `(N / MaxConcurrency) × avgPerCallLatency`, not `N × avgPerCallLatency`.
- An integration-style unit test demonstrates that with a fake `IReceivedInvoicesClient` whose `GetInvoiceByIdAsync` sleeps 200 ms, fetching 10 invoices completes in well under 2 s (allowing for scheduler jitter; assert < 800 ms with concurrency ≥ 5).
- The handler's public contract (`ClassifyInvoicesRequest` / `ClassifyInvoicesResponse`) is unchanged.

### FR-2: Bounded concurrency

The parallel fetch must not issue an unbounded number of simultaneous calls to Flexi.

**Acceptance criteria:**
- Concurrency is capped at a configurable limit (default **8**, see NFR-3 for rationale).
- The limit is implemented via `SemaphoreSlim` (or equivalent throttle), not raw `Task.WhenAll(ids.Select(...))` over arbitrary N.
- A unit test with a tracking fake client verifies that at most N concurrent in-flight calls exist at any moment when the limit is N.

### FR-3: Preserve per-invoice error reporting

Each id that returns `null` (not found) must continue to produce one entry in `response.Errors` and one `"Invoice {id} not found"` message in `response.ErrorMessages`, just like the current implementation.

**Acceptance criteria:**
- For input `[A, B, C]` where B is missing: response has `Errors = 1`, `ErrorMessages` contains exactly `"Invoice B not found"`, and `invoicesToClassify` contains A and C.
- A warning log entry `"Invoice {InvoiceId} not found"` is emitted for each missing id (matches current `_logger.LogWarning` behavior at line 47).

### FR-4: Surface fetch-phase exceptions per invoice

If `GetInvoiceByIdAsync` throws for a given id (e.g., Flexi network failure), that single id must fail without aborting the rest of the batch.

**Acceptance criteria:**
- A unit test with a fake client that throws for one id verifies: `response.Errors` is incremented for the failing id, `ErrorMessages` contains a descriptive message including the id and exception message, an `_logger.LogError` entry is emitted, and the remaining ids are still fetched and classified.
- The handler does not propagate the exception out of the fetch phase.
- *Note:* This is a small behavior addition vs. today — the current code does not try/catch fetch failures, so a single Flexi error currently fails the whole request. Tightening this is a natural fit for this change since we're already restructuring the loop.

### FR-5: Deterministic error ordering (best-effort)

`ErrorMessages` should remain readable when fetch errors are interleaved with classification errors.

**Acceptance criteria:**
- All fetch-phase errors (not-found, fetch exceptions) are appended to `ErrorMessages` **before** any classification-phase errors.
- Within the fetch phase, errors appear in the order of `request.InvoiceIds` (i.e., results are reassembled in input order before reporting), so log diffs remain stable across runs.

### FR-6: Batch mode unchanged

The path for `request.InvoiceIds == null || Count == 0` (batch mode using `GetUnclassifiedInvoicesAsync`) is not modified.

**Acceptance criteria:**
- Code path at `ClassifyInvoicesHandler.cs:56-61` is byte-identical post-change except for any incidental refactor of surrounding variables.
- Existing batch-mode behavior (single `_invoicesClient.GetUnclassifiedInvoicesAsync()` call) is unchanged.

## Non-Functional Requirements

### NFR-1: Performance

- **Target:** P50 latency for a 20-id request drops from ~15 s to **≤ 3 s**, assuming Flexi `GetAsync` averages ~500 ms per call.
- **Throughput:** No regression in the batch mode path (no change there).
- Verified via a local benchmark/test using a configurable-delay fake `IReceivedInvoicesClient`.

### NFR-2: Reliability

- A fetch failure for one id must not affect the others (FR-4).
- A `CancellationToken` (the `cancellationToken` parameter already on `Handle`) must be passed into the throttle/wait so the request can be cancelled cleanly. *Constraint:* `IReceivedInvoicesClient.GetInvoiceByIdAsync(string)` does not currently accept a `CancellationToken`. We will not modify the interface in this change (out of scope, see Out of Scope) — cancellation will be honored at the throttle/`Task.WhenAll` boundary, not propagated into Flexi calls themselves.

### NFR-3: Resource bounds

- Default max concurrency: **8**. Rationale: balances responsiveness against Flexi API load. The existing parallel patterns in this codebase (e.g., `FlexiManufactureHistoryClient.cs:57` uses `_stockItemsMovementClient.GetAsync` inside what looks like a throttled loop — see Open Questions OQ-1) suggest similar caution. 8 is conservative enough that it should not trip Flexi rate-limits even in worst-case manual triggers.
- The limit must be implemented as a private constant in the handler (not yet promoted to configuration; see Out of Scope).

### NFR-4: Observability

- Existing log lines (`"Starting classification of {Count} specific invoices"`, etc.) are preserved verbatim.
- Add one new debug-level log: `"Fetched {FetchedCount}/{RequestedCount} invoices in {ElapsedMs}ms"` immediately after the parallel fetch completes, to make App Insights regression detection trivial.

### NFR-5: Security

No security surface change. The endpoint authorization, validation, and data exposure are unmodified. No new external inputs are accepted.

### NFR-6: Backward compatibility

- `IReceivedInvoicesClient` interface is unchanged.
- `ClassifyInvoicesRequest` / `ClassifyInvoicesResponse` DTOs are unchanged (no OpenAPI client regeneration needed).
- Existing callers (`InvoiceClassificationController.ClassifyInvoices` and `ClassifySingleInvoice` at `InvoiceClassificationController.cs:68-73,119-129`, plus `InvoiceClassificationJob`) continue to work without modification.

## Data Model

No data-model changes. The handler operates on existing types:

- `ReceivedInvoiceDto` (`Anela.Heblo.Domain.Features.InvoiceClassification`) — unchanged.
- `ClassifyInvoicesRequest` — unchanged.
- `ClassifyInvoicesResponse` — unchanged.
- `IReceivedInvoicesClient` — unchanged.

## API / Interface Design

No public API changes. The change is entirely within `ClassifyInvoicesHandler.Handle`.

**Sketch of replaced section** (`ClassifyInvoicesHandler.cs:36-55`):

```csharp
if (request.InvoiceIds != null && request.InvoiceIds.Count > 0)
{
    const int maxConcurrency = 8;
    using var throttle = new SemaphoreSlim(maxConcurrency);
    var sw = Stopwatch.StartNew();

    var fetchTasks = request.InvoiceIds.Select(async id =>
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var invoice = await _invoicesClient.GetInvoiceByIdAsync(id);
                return (Id: id, Invoice: invoice, Error: (string?)null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching invoice {InvoiceId}", id);
                return (Id: id, Invoice: (ReceivedInvoiceDto?)null, Error: ex.Message);
            }
        }
        finally
        {
            throttle.Release();
        }
    }).ToList();

    var fetchResults = await Task.WhenAll(fetchTasks);
    sw.Stop();
    _logger.LogDebug("Fetched {FetchedCount}/{RequestedCount} invoices in {ElapsedMs}ms",
        fetchResults.Count(r => r.Invoice != null), request.InvoiceIds.Count, sw.ElapsedMilliseconds);

    invoicesToClassify = new List<ReceivedInvoiceDto>(fetchResults.Length);
    foreach (var r in fetchResults) // input-order traversal preserves FR-5
    {
        if (r.Invoice != null)
        {
            invoicesToClassify.Add(r.Invoice);
        }
        else
        {
            response.Errors++;
            if (r.Error != null)
            {
                errorMessages.Add($"Invoice {r.Id}: fetch failed: {r.Error}");
            }
            else
            {
                errorMessages.Add($"Invoice {r.Id} not found");
                _logger.LogWarning("Invoice {InvoiceId} not found", r.Id);
            }
        }
    }

    _logger.LogInformation("Starting classification of {Count} specific invoices", invoicesToClassify.Count);
}
```

## Dependencies

- **`IReceivedInvoicesClient`** (existing) — must remain thread-safe under concurrent calls. Verified: `FlexiReceivedInvoicesClient` is registered scoped/transient (see `FlexiAdapterServiceCollectionExtensions.cs`); the underlying Flexi `IReceivedInvoiceClient` from `Rem.FlexiBeeSDK` is HTTP-based and stateless per call. Confirm in implementation that the registration lifetime allows parallel use.
- **`SemaphoreSlim`** (BCL) — for concurrency throttling. No new package.
- **`System.Diagnostics.Stopwatch`** (BCL) — for the new debug log.

## Out of Scope

- Adding a `GetInvoicesByIdsAsync(IEnumerable<string>)` method to `IReceivedInvoicesClient` and/or the Flexi SDK. The brief lists this as an alternative; it is rejected for this change because the Flexi SDK does not expose a native bulk endpoint, so a "bulk" method would just be the same parallel loop relocated, with no additional benefit and a wider blast radius.
- Adding `CancellationToken` to `IReceivedInvoicesClient.GetInvoiceByIdAsync`. Useful but a separate, broader change touching the Flexi adapter and any other implementations.
- Parallelizing the **classification** loop (`ClassifyInvoicesHandler.cs:65-105`). Classification writes history rows, calls a separate Flexi client to update state, and depends on rule ordering — concurrency there has correctness risks and is out of scope.
- Promoting `maxConcurrency` to `IConfiguration`. Defer until we have evidence the default needs tuning per environment.
- Adding retries on transient Flexi failures. Out of scope.
- Frontend changes. The page (`frontend/src/pages/InvoiceClassification/InvoiceClassificationPage.tsx`) is unaffected.

## Open Questions

None.

## Status: COMPLETE