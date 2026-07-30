# Design — FlexiBee ERP dependency latency: circuit breaker + evidence-first investigation

No UI/UX section: this task has no new screen, component, or interaction. The
only user-visible change (FR-3) is a Czech error-message string surfaced
through the **existing** `ManufactureOrder` note / `ManualActionRequired`
mechanism already rendered by the manufacture order UI — no new frontend
component, no new API contract, no wireframe to draw.

## Component design

### FR-1 — Attribution query (investigation, no code)

No new component. This is a one-shot script invocation, not a code change:

```bash
./docs/routines/telemetry-anomaly/appinsights-query.sh --timespan P7D '
dependencies
| where target == "petra-tesarikova.flexibee.eu"
| summarize calls=count(),
            p50=round(percentile(duration,50),0),
            p95=round(percentile(duration,95),0),
            p99=round(percentile(duration,99),0),
            maxdur=round(max(duration),0)
  by name, operation_Name
| order by p99 desc
'
```

Output is consumed directly by FR-2 (below) — there is no intermediate
artifact format to design beyond "paste the resulting table into the doc."

### FR-2 — `docs/integrations/flexibee-api.md`

New file, following the section shape already established by
`docs/integrations/shoptet-api.md` (Overview → Auth → per-area findings).
Structure:

```
# FlexiBee (ABRA) API — Integration Findings

## 1. Overview
  - Adapter: Anela.Heblo.Adapters.Flexi, client Rem.FlexiBeeSDK.Client
  - Target host: petra-tesarikova.flexibee.eu

## 2. Latency findings (2026-07-28)
  - Resource ranking table (FR-1 output, by `name`/`operation_Name`)
  - Confirm-path vs. background-path split, with the ~2%/98% call-volume math
  - Explanation (or confirmed non-explanation) of the 300s -> 75s max-duration
    drop between #2987 and now

## 3. Decision
  - What is being fixed (FR-3) and why
  - FR-4 status: scoped as follow-up issue #<n>, or marked not-applicable with
    the background-calls follow-up recommendation instead
```

This is a documentation artifact, not a code component — its only "interface"
is being the required reference doc `SubmitManufactureHandler`'s change (FR-3)
and any future FlexiBee work must point back to, per `CLAUDE.md`'s "Shoptet
API findings must be documented before use" convention extended to FlexiBee.

### FR-3 — Circuit breaker around the ERP submit call

**Precedent chosen:** `CatalogResilienceService`
(`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogResilienceService.cs`)
is an existing, in-repo Polly v8 (`ResiliencePipeline`) retry+circuit-breaker+timeout
wrapper already running in production for another external dependency. It is a
closer, more concrete precedent than the Plaud plan doc referenced in the plan
step (which covers retry-suppression, not a circuit breaker) — FR-3 mirrors its
shape exactly rather than inventing a new one.

**New component: `IManufactureErpResilienceService`**

```
backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/
  ManufactureErpResilienceService.cs
```

```csharp
public interface IManufactureErpResilienceService
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);
}
```

- Implementation: a `ResiliencePipelineBuilder` with **only** `AddCircuitBreaker`
  (no retry, no inner timeout) — per the plan's NFR, this is about bounding
  blast radius on repeated failure, not adding latency via retries on a
  dependency that is already slow. The existing `ErpTimeoutSeconds`
  `CancelAfter` in `SubmitManufactureHandler.CreateLinkedCts` remains the
  per-call timeout; the breaker sits around it.
- `ShouldHandle`: `HttpRequestException`, `TaskCanceledException`, and
  `OperationCanceledException` where the token wasn't the caller's own
  (i.e. the ERP timeout fired) — same predicate shape as
  `CatalogResilienceService`.
- On `BrokenCircuitException`, the service catches it and rethrows a new,
  dedicated `ManufactureErpUnavailableException(string operationName, Exception inner)`
  — a **typed** exception (not a message-matched one), because it originates
  entirely in our own code and a message-content filter (the pattern the other
  `IManufactureErrorFilter`s use for FlexiBee SDK exceptions) would be an
  unnecessary indirection here.
- Registered `AddSingleton<IManufactureErpResilienceService, ManufactureErpResilienceService>()`
  in `ManufactureModule.cs`, next to the other Manufacture service
  registrations — singleton because the breaker state must persist across
  requests (same lifetime choice as `CatalogResilienceService`).

**Config additions — `ManufactureErpOptions.cs`:**

```csharp
public int ErpCircuitBreakerMinimumThroughput { get; set; } = 3;
public double ErpCircuitBreakerFailureRatio { get; set; } = 0.5;
public int ErpCircuitBreakerSamplingDurationSeconds { get; set; } = 60;
public int ErpCircuitBreakerBreakDurationSeconds { get; set; } = 30;
```

Bound from the existing `ManufactureErp` configuration section (no new
`services.Configure<>` call needed — same options class). Defaults mirror
`CatalogResilienceService`'s hardcoded values, now made configurable per the
plan's open question, in the same units/shape as the existing
`ErpTimeoutSeconds` knob.

**`SubmitManufactureHandler` change** (`SubmitManufactureHandler.cs:46-47`):

```csharp
var clientResponse = await _resilienceService.ExecuteAsync(
    ct => _manufactureClient.SubmitManufactureAsync(request.ToClientRequest(), ct),
    "SubmitManufacture",
    cts.Token);
```

New constructor dependency `IManufactureErpResilienceService _resilienceService`.
No other change to the method — the existing
`catch (Exception ex) when (ex is not OperationCanceledException)` block
already catches `ManufactureErpUnavailableException` (it's a plain `Exception`
subtype) and routes it through `_errorTransformer.Transform(ex)`, so no new
try/catch is needed in the handler itself.

**New error filter:**

```
backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/
  ErpCircuitOpenFilter.cs
```

```csharp
public class ErpCircuitOpenFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception is ManufactureErpUnavailableException;

    public string Transform(Exception exception) =>
        "FlexiBee je aktuálně nedostupný nebo neodpovídá včas (opakované chyby). "
        + "Zkuste akci zopakovat za chvíli; pokud problém přetrvává, kontaktujte správce systému.";
}
```

Picked up automatically by the existing `services.Scan(...)` registration in
`ManufactureModule.cs` (no DI change needed for the filter itself — it's
discovered by namespace, same as every other filter in that folder).

**Downstream propagation — already exists, unchanged:**
`ConfirmProductCompletionWorkflow.TransitionToCompletedAsync`
(`ConfirmProductCompletionWorkflow.cs:249-278`) already reads
`submitResult.Success` / `submitResult.UserMessage` and sets
`ManualActionRequired = !submitResult.Success || bomFailures.Count > 0` plus
writes the note. **No workflow code changes are needed** — a circuit-open
response flows through the exact same "ERP submission failed" path a timeout
or any other FlexiBee error already takes today. This was verified by reading
the workflow, not assumed.

**Observability:** `OnOpened`/`OnClosed`/`OnHalfOpened` callbacks (same shape
as `CatalogResilienceService`) log at Warning/Information via
`ILogger<ManufactureErpResilienceService>`, landing in App Insights `traces`
— queryable by the telemetry-anomaly routine to confirm the fix without
re-filing the same signal.

### FR-4 — Conditional design note (not implemented here)

If FR-1 shows the confirm endpoints materially drive dependency-level p95/p99:
a short markdown design note (`docs/features/` or attached to the new GitHub
issue body — not code) covering:

- New `ManufactureOrderState` value (e.g. `PendingErpSync`) inserted between
  the current completion step and `Completed`.
- A background retry job (mirrors existing recurring-job patterns already in
  `Anela.Heblo.Application.Features.BackgroundJobs`) that retries
  `SubmitManufactureAsync` for orders in `PendingErpSync`.
- Reuse of the existing `ManualActionRequired`/note mechanism for terminal
  failures after N retries, rather than inventing a new error-surfacing path.
- Explicitly out of scope for design here: exact retry count/backoff,
  frontend polling UX for the pending state — left to the follow-up issue's
  own design step.

If FR-1 shows background/unattributed calls dominate instead, no design note
is produced; FR-2's doc records this and points at batching/caching/rate-limiting
the background sync clients (`FlexiAnalyticsSyncJob`, `FlexiStockClient`,
`FlexiLotsClient`) as the next follow-up instead.

## Data schemas

**No database schema changes.** No new persisted entities for FR-1–FR-3.

**Config schema (`ManufactureErp` section, bound to `ManufactureErpOptions`)** —
additive, backward-compatible (existing deployed config with no new keys keeps
today's `CatalogResilienceService`-equivalent defaults):

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ErpTimeoutSeconds` | int | 60 | *(existing, unchanged)* per-call timeout |
| `ManufactureGroupId` | string? | null | *(existing, unchanged)* |
| `ErpCircuitBreakerMinimumThroughput` | int | 3 | min calls in sampling window before ratio is evaluated |
| `ErpCircuitBreakerFailureRatio` | double | 0.5 | fraction of failures in window that opens the breaker |
| `ErpCircuitBreakerSamplingDurationSeconds` | int | 60 | rolling window for failure-ratio calculation |
| `ErpCircuitBreakerBreakDurationSeconds` | int | 30 | how long the breaker stays open before half-open probe |

**Exception type (new, internal — not serialized/DTO):**

```csharp
public class ManufactureErpUnavailableException : Exception
{
    public string OperationName { get; }
    public ManufactureErpUnavailableException(string operationName, Exception inner)
        : base($"FlexiBee ERP unavailable for operation '{operationName}'", inner) { ... }
}
```

**Request/response DTOs: no changes.** `SubmitManufactureRequest`,
`SubmitManufactureResponse`, `SubmitManufactureClientRequest/Response` are
untouched — `SubmitManufactureResponse.UserMessage` already carries whatever
string the (now one-more-filter) `IManufactureErrorTransformer` produces, and
`SubmitManufactureResponse(ex)` (the failure constructor) already exists and
is reused as-is.

**FR-1 output shape** (ranked table, recorded verbatim into FR-2's doc, not a
persisted schema):

| name | operation_Name | calls | p50 | p95 | p99 | maxdur |
|---|---|---|---|---|---|---|

## Dependencies and scope

Unchanged from the plan step — see `plan-01.md` for full scope,
non-functional requirements, and open questions. This design resolves the
plan's "circuit breaker failure threshold/duration" open question by adopting
`CatalogResilienceService`'s exact defaults (made configurable) rather than
inventing new numbers, since that pipeline is an already-validated,
production-proven shape in this codebase.
