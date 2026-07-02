# Specification: Open/Closed DQT Runner Dispatch (RunDqtHandler / GetDqtRunDetailHandler)

## Summary
`RunDqtHandler` and `GetDqtRunDetailHandler` currently dispatch on `DqtTestType` using a binary `if (TestType == IssuedInvoiceComparison) { invoice path } else { drift path }`, silently routing any future non-invoice, non-drift `DqtTestType` into the drift runner where it fails at runtime with a misleading error. This spec replaces the binary dispatch with an explicit, extensible resolution mechanism — a shared `IDqtJobRunner` abstraction for `RunDqtHandler`, and a fail-fast `NotSupportedException` guard for `GetDqtRunDetailHandler` — so unregistered test types fail clearly at the point of dispatch rather than being silently mis-routed.

## Background
`DqtTestType` (`backend/src/Anela.Heblo.Domain/Features/DataQuality/DqtTestType.cs`) has three values today: `IssuedInvoiceComparison`, `ProductPairing`, `StockWriteBackReconciliation`. The latter two ("drift" types) are already handled in an Open/Closed-compliant way: `IDriftDqtComparer` (`backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDriftDqtComparer.cs`) exposes a `DqtTestType TestType { get; }` discriminator, is implemented by `ProductPairingDqtComparer` and `StockWriteBackDqtComparer`, and `DriftDqtJobRunner` (`backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs`) resolves the correct comparer via `IEnumerable<IDriftDqtComparer>` injection + `.SingleOrDefault(c => c.TestType == run.TestType)`, throwing `InvalidOperationException` if none matches. Adding a new drift type requires only a new `IDriftDqtComparer` implementation and a DI registration — no handler changes.

The remaining gap is one level up: the top-level split between "invoice" and "drift" runs is hardcoded as a binary `if/else` in two places:
- `RunDqtHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs`, lines 49–58): resolves either `IInvoiceDqtJobRunner` or `IDriftDqtJobRunner` from the DI scope based on `request.TestType == DqtTestType.IssuedInvoiceComparison`.
- `GetDqtRunDetailHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs`, lines 38–57): returns invoice-shaped `Results` if `run.TestType == DqtTestType.IssuedInvoiceComparison`, otherwise unconditionally falls through to drift-shaped `DriftResults`/`TotalDriftResults` — with no `else` keyword, i.e. an implicit "anything else is drift" assumption.

Both `IInvoiceDqtJobRunner` and `IDriftDqtJobRunner` are registered today as distinct scoped services in `DataQualityModule.cs` (`AddScoped<IInvoiceDqtJobRunner, InvoiceDqtJobRunner>()`, `AddScoped<IDriftDqtJobRunner, DriftDqtJobRunner>()`), with no shared interface connecting them. This means a hypothetical `DqtTestType.SupplierAudit = 4` with its own new runner would be silently routed into `IDriftDqtJobRunner`, which would then throw `InvalidOperationException: No IDriftDqtComparer registered for SupplierAudit` — a confusing error that points at the wrong layer of the system and gives no signal that the top-level dispatch itself is the actual defect.

This spec closes that gap by extending the existing `IDriftDqtComparer`-style "discriminator + resolve from collection" pattern up one level, to the runner-selection point in `RunDqtHandler`, and by making `GetDqtRunDetailHandler`'s implicit else-branch an explicit, fail-fast branch.

## Functional Requirements

### FR-1: Introduce a shared `IDqtJobRunner` abstraction with explicit dispatch capability
Add a new interface `IDqtJobRunner` in `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/` that both `InvoiceDqtJobRunner` and `DriftDqtJobRunner` implement, exposing:
- A way to run the job (`Task RunAsync(Guid runId, CancellationToken ct = default)` — matching the existing `RunAsync` signatures already present on both `IInvoiceDqtJobRunner` and `IDriftDqtJobRunner`, so both classes can implement it without behavioral change).
- A way to determine, given a `DqtTestType`, whether a given runner instance handles it: `bool CanHandle(DqtTestType testType)`.

`CanHandle` is chosen over a single `DqtTestType TestType { get; }` property (as used by `IDriftDqtComparer`) because `DriftDqtJobRunner` is a single instance that legitimately handles multiple `DqtTestType` values (`ProductPairing` and `StockWriteBackReconciliation`, and any future drift type with a registered `IDriftDqtComparer`) — a single-value `TestType` property would be ambiguous or would require `DriftDqtJobRunner` to enumerate/duplicate the set of types its inner `IDriftDqtComparer` collection already covers, creating a second source of truth that can drift out of sync with the `IDriftDqtComparer` registrations.

Implementations:
- `InvoiceDqtJobRunner.CanHandle(DqtTestType testType) => testType == DqtTestType.IssuedInvoiceComparison;`
- `DriftDqtJobRunner.CanHandle(DqtTestType testType) => _comparers.Any(c => c.TestType == testType);` (delegates to the already-injected `IEnumerable<IDriftDqtComparer>`, keeping a single source of truth for which drift types are supported).

The existing `IInvoiceDqtJobRunner` and `IDriftDqtJobRunner` interfaces are retained unchanged (no call sites outside the two handlers in scope are known to depend on them being removed; removing them is out of scope — see Out of Scope). `InvoiceDqtJobRunner` and `DriftDqtJobRunner` each implement both their existing single-purpose interface and the new `IDqtJobRunner` interface.

**Acceptance criteria:**
- `IDqtJobRunner` interface exists in `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/` with `Task RunAsync(Guid runId, CancellationToken ct = default)` and `bool CanHandle(DqtTestType testType)`.
- `InvoiceDqtJobRunner` implements `IInvoiceDqtJobRunner, IDqtJobRunner`; `InvoiceDqtJobRunner.CanHandle` returns `true` only for `DqtTestType.IssuedInvoiceComparison`.
- `DriftDqtJobRunner` implements `IDriftDqtJobRunner, IDqtJobRunner`; `DriftDqtJobRunner.CanHandle` returns `true` for exactly the set of `DqtTestType` values that have a registered `IDriftDqtComparer` at the time of the call.
- No behavior change to either class's existing `RunAsync` logic.

### FR-2: Register both runners under `IDqtJobRunner` in `DataQualityModule.cs`
Add `services.AddScoped<IDqtJobRunner, InvoiceDqtJobRunner>();` and `services.AddScoped<IDqtJobRunner, DriftDqtJobRunner>();` to `AddDataQualityModule` in `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs`, alongside (not replacing) the existing `AddScoped<IInvoiceDqtJobRunner, InvoiceDqtJobRunner>()` and `AddScoped<IDriftDqtJobRunner, DriftDqtJobRunner>()` registrations.

**Acceptance criteria:**
- Resolving `IEnumerable<IDqtJobRunner>` from the container yields exactly two instances: one `InvoiceDqtJobRunner`, one `DriftDqtJobRunner`.
- Existing resolution of `IInvoiceDqtJobRunner` and `IDriftDqtJobRunner` individually continues to work unchanged (verify no regression in any code path still using those interfaces directly, if any exist outside the two handlers in scope).

### FR-3: Replace binary dispatch in `RunDqtHandler` with `IDqtJobRunner` resolution
In `RunDqtHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs`, lines 46–59), replace the `if (request.TestType == DqtTestType.IssuedInvoiceComparison) { ... } else { ... }` block with:
```csharp
_ = Task.Run(async () =>
{
    using var scope = _scopeFactory.CreateScope();
    var runner = scope.ServiceProvider
        .GetServices<IDqtJobRunner>()
        .SingleOrDefault(r => r.CanHandle(request.TestType))
        ?? throw new InvalidOperationException($"No IDqtJobRunner registered for {request.TestType}");
    await runner.RunAsync(run.Id);
}, CancellationToken.None);
```
Note this runs inside the existing fire-and-forget `Task.Run` (not the outer `try/catch` around `Handle`'s synchronous body), so an unmatched `TestType` here throws asynchronously, unobserved by the caller of `Handle` — matching current behavior where the fire-and-forget task's own exceptions are already not surfaced to the HTTP response (the run is created and `Success = true` is returned before `RunAsync` completes or fails). This is an existing characteristic of the fire-and-forget design, not a regression introduced by this change; it is flagged here for awareness, not fixed (see Out of Scope).

**Acceptance criteria:**
- For `request.TestType == DqtTestType.IssuedInvoiceComparison`, `InvoiceDqtJobRunner.RunAsync` is invoked (same as current behavior).
- For `request.TestType` equal to `ProductPairing` or `StockWriteBackReconciliation`, `DriftDqtJobRunner.RunAsync` is invoked (same as current behavior).
- For a `DqtTestType` value with no `IDqtJobRunner` whose `CanHandle` returns `true` (e.g. a hypothetical future enum value with no runner registered), an `InvalidOperationException` with a message identifying the unhandled `TestType` is thrown inside the background `Task.Run`, and neither `InvoiceDqtJobRunner.RunAsync` nor `DriftDqtJobRunner.RunAsync` is invoked.
- `.SingleOrDefault` (not `.FirstOrDefault`) is used, so that if a future misconfiguration causes two `IDqtJobRunner` registrations to both claim `CanHandle == true` for the same `TestType`, this is surfaced as an `InvalidOperationException` (from `SingleOrDefault`'s built-in ambiguity check) rather than silently picking the first match.

### FR-4: Replace implicit else-branch in `GetDqtRunDetailHandler` with explicit, fail-fast dispatch
In `GetDqtRunDetailHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs`, lines 38–57), replace the `if (run.TestType == DqtTestType.IssuedInvoiceComparison) { return invoice-shaped response } ` followed by an unconditional fall-through to drift-shaped response, with an explicit three-way dispatch that fails fast on unrecognized types. Recommended shape (exact syntax — `switch` vs. `if/else if/else` — left to the implementer, but the fail-fast branch is required):
```csharp
if (run.TestType == DqtTestType.IssuedInvoiceComparison)
{
    return new GetDqtRunDetailResponse
    {
        Success = true,
        Run = _mapper.Map<DqtRunDto>(run),
        Results = _mapper.Map<List<InvoiceDqtResultDto>>(run.Results)
    };
}

if (run.TestType is DqtTestType.ProductPairing or DqtTestType.StockWriteBackReconciliation)
{
    var (driftItems, driftTotal) = await _repository.GetDriftResultsAsync(
        run.Id, request.ResultPage, request.ResultPageSize, cancellationToken);

    return new GetDqtRunDetailResponse
    {
        Success = true,
        Run = _mapper.Map<DqtRunDto>(run),
        DriftResults = _mapper.Map<List<DqtDriftResultDto>>(driftItems),
        TotalDriftResults = driftTotal
    };
}

throw new NotSupportedException($"No result-shaping logic registered for DqtTestType {run.TestType}");
```
The `NotSupportedException` is caught by the handler's existing outer `try/catch (Exception ex)` (lines 59–67 in current source), which logs the error and returns `Success = false, ErrorCode = ErrorCodes.Exception` — i.e. the failure surfaces to the API caller as a normal error response rather than an unhandled exception or a wrongly-shaped success response. No new `ErrorCodes` entry is required unless the architect/reviewer decides a more specific error code is warranted (see Open Questions).

Listing `ProductPairing` and `StockWriteBackReconciliation` explicitly (rather than a catch-all `else`) is intentional: it makes the drift-type set visible at the call site and ensures any future `DqtTestType` addition must be explicitly added to this handler (or the handler must be redesigned to consult `IDqtJobRunner`/`IDriftDqtComparer` metadata directly — see Open Questions) rather than silently falling into either branch.

**Acceptance criteria:**
- `run.TestType == DqtTestType.IssuedInvoiceComparison` returns the invoice-shaped response (unchanged from current behavior).
- `run.TestType` equal to `ProductPairing` or `StockWriteBackReconciliation` returns the drift-shaped response (unchanged from current behavior).
- Any other `DqtTestType` value (including any value added to the enum in the future without a corresponding handler update) causes `Handle` to return `Success = false, ErrorCode = ErrorCodes.Exception` (via the existing catch block), not a wrongly-shaped `Success = true` response and not an unhandled exception escaping `Handle`.
- The thrown exception type is `NotSupportedException` (or equivalent explicit, distinctly-named exception — not a generic `Exception`), and its message identifies the unhandled `TestType` value, to aid diagnosis in logs.

### FR-5: Update/add unit tests covering the new dispatch behavior
`backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs` and `backend/test/Anela.Heblo.Tests/Features/DataQuality/GetDqtRunDetailHandlerTests.cs` already exist and presumably cover the current invoice/drift branches; they must be reviewed and updated to reflect the new `IDqtJobRunner`-based resolution (for `RunDqtHandlerTests`) and the explicit three-way dispatch (for `GetDqtRunDetailHandlerTests`), plus new test cases for the fail-fast path.

**Acceptance criteria:**
- `RunDqtHandlerTests` includes a test asserting that when `TestType == IssuedInvoiceComparison`, the resolved-and-invoked runner is the one whose `CanHandle` matched (i.e., `InvoiceDqtJobRunner`'s `RunAsync` is called), and equivalently for a drift type invoking `DriftDqtJobRunner`.
- `RunDqtHandlerTests` includes a test asserting that if no registered `IDqtJobRunner` has `CanHandle == true` for the given `TestType`, an `InvalidOperationException` is thrown (e.g. constructed with a test double `IDqtJobRunner` set that doesn't cover a given enum value, or — if feasible without excessive test complexity given the fire-and-forget `Task.Run` — asserted via an unobserved task exception/awaiting a test hook).
- `GetDqtRunDetailHandlerTests` includes a test asserting that a `DqtRun` with an unrecognized/unmapped `TestType` (e.g. a new enum value not yet wired into the handler, or a mocked/constructed `DqtRun` with an out-of-range enum value) results in `Success = false` and `ErrorCode = ErrorCodes.Exception`, not a partially-populated success response.
- All existing passing tests for the invoice and drift paths continue to pass unmodified in behavior (assertions may need updating for any renamed mocks/interfaces, but expected response shapes are unchanged).
- `dotnet build` and `dotnet format` succeed; all tests in `Anela.Heblo.Tests` pass.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact expected. `GetServices<IDqtJobRunner>().SingleOrDefault(...)` resolves at most 2 registered instances (today) — negligible overhead compared to the existing single `GetRequiredService<T>()` call it replaces. No change to `GetDqtRunDetailHandler`'s query/mapping cost; the dispatch logic change is a pure control-flow restructuring with the same number of branches evaluated in the common case.

### NFR-2: Security
No change to authentication, authorization, or data sensitivity. This is an internal dispatch-logic refactor with no new external inputs, no new persisted data, and no API contract change. `DqtTestType` values already flow through existing validated request/persisted-entity paths.

### NFR-3: Backward compatibility
No breaking changes: `GetDqtRunDetailResponse` DTO shape (`Run`, `Results`, `DriftResults`, `TotalDriftResults`, inherited `BaseResponse` fields) is unchanged. `RunDqtResponse` DTO shape is unchanged. Existing `IInvoiceDqtJobRunner` and `IDriftDqtJobRunner` interfaces and their DI registrations are retained, so any other code (if it exists) depending on resolving those interfaces directly continues to work unaffected.

## Data Model
No data model changes. `DqtTestType` enum (`backend/src/Anela.Heblo.Domain/Features/DataQuality/DqtTestType.cs`) is unchanged by this spec — this refactor only changes how existing enum values are dispatched in application-layer handlers, not the enum itself. No new database tables, columns, or migrations.

Entities referenced (unchanged):
- `DqtRun` — has `TestType : DqtTestType`, `Results : ICollection<InvoiceDqtResult>` (invoice-specific), plus drift results retrieved separately via `IDqtRunRepository.GetDriftResultsAsync`.
- `IDriftDqtComparer` implementations (`ProductPairingDqtComparer`, `StockWriteBackDqtComparer`) — each declares its own `DqtTestType TestType`.

## API / Interface Design

New interface:
```csharp
// backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtJobRunner.cs
public interface IDqtJobRunner
{
    bool CanHandle(DqtTestType testType);
    Task RunAsync(Guid runId, CancellationToken ct = default);
}
```

Modified DI registrations (`DataQualityModule.cs`, additive):
```csharp
services.AddScoped<IDqtJobRunner, InvoiceDqtJobRunner>();
services.AddScoped<IDqtJobRunner, DriftDqtJobRunner>();
```

Modified dispatch (`RunDqtHandler.Handle`, inside the existing fire-and-forget `Task.Run`):
```csharp
var runner = scope.ServiceProvider
    .GetServices<IDqtJobRunner>()
    .SingleOrDefault(r => r.CanHandle(request.TestType))
    ?? throw new InvalidOperationException($"No IDqtJobRunner registered for {request.TestType}");
await runner.RunAsync(run.Id);
```

Modified dispatch (`GetDqtRunDetailHandler.Handle`): explicit three-branch structure (invoice / known-drift-types / fail-fast `NotSupportedException`) as detailed in FR-4.

No public API/HTTP contract changes: `RunDqtRequest`/`RunDqtResponse` and `GetDqtRunDetailRequest`/`GetDqtRunDetailResponse` DTOs are unchanged in shape. No new endpoints. No frontend changes required — this is purely internal Application-layer dispatch logic behind the existing MediatR handlers.

## Dependencies
- Existing `Microsoft.Extensions.DependencyInjection` (`IServiceScopeFactory`, `GetServices<T>()`) — already used in `RunDqtHandler` today (`GetRequiredService<T>()`), no new package dependency.
- No external service dependencies introduced.
- Depends on the existing `IDriftDqtComparer` collection registered in `DataQualityModule.cs` for `DriftDqtJobRunner.CanHandle` to correctly reflect supported drift types.

## Out of Scope
- Removing or deprecating `IInvoiceDqtJobRunner` / `IDriftDqtJobRunner` — both are retained alongside the new `IDqtJobRunner` to avoid touching any other (unverified) call sites that may depend on them.
- Fixing the pre-existing fire-and-forget exception-swallowing behavior in `RunDqtHandler` (exceptions thrown inside the background `Task.Run`, including the new `InvalidOperationException` for an unmatched `TestType`, are not surfaced to the HTTP caller or persisted onto the `DqtRun` record as a `Fail()` state — this matches current behavior for any exception thrown before a runner's own internal `try/catch`/`run.Fail(...)` logic takes over, and is not being changed here).
- Any change to `DqtTestType` enum values themselves (no new test types are being added by this spec — it only makes the dispatch mechanism ready for a future addition).
- Any change to `IDriftDqtComparer`, `ProductPairingDqtComparer`, or `StockWriteBackDqtComparer` — the intra-drift extensibility pattern is already correct and untouched.
- Frontend/UI changes — none required; this is a backend-only internal refactor.
- Database schema/migration changes — none required.
- Adding a new, more specific `ErrorCodes` value for the `GetDqtRunDetailHandler` fail-fast path (falls back to existing `ErrorCodes.Exception` unless the architect decides otherwise during implementation).

## Open Questions
- Should `GetDqtRunDetailHandler`'s result-shaping dispatch instead be driven by metadata exposed from `IDqtJobRunner`/`IDriftDqtComparer` (e.g., an `IDqtJobRunner` or comparer exposing "which result DTO shape does this test type produce") rather than a handler-local, manually-maintained `ProductPairing or StockWriteBackReconciliation` list? FR-4 as specified keeps the explicit list (simpler, matches the brief's "minimum fix" framing) but this reintroduces a place that must be updated when a new drift-category type is added — arguably an acceptable, smaller Open/Closed gap than the current binary invoice/not-invoice split, since the failure mode is now a fail-fast exception rather than silent mis-routing. Confirm this is an acceptable trade-off, or specify the metadata-driven approach if the architect prefers full closure.
- Should the fail-fast `NotSupportedException` in `GetDqtRunDetailHandler` map to a distinct `ErrorCodes` value (e.g. `ErrorCodes.DqtUnsupportedTestType`) instead of falling back to the generic `ErrorCodes.Exception`, to make this failure mode distinguishable in logs/monitoring from unrelated exceptions? Default assumption in this spec: reuse `ErrorCodes.Exception` (no new error code) to minimize surface area, per "Out of Scope."
- Should `RunDqtHandler`'s background `Task.Run` be changed to persist a `run.Fail(...)` state (and call `_repository.SaveChangesAsync`) when no `IDqtJobRunner` matches, so the `DqtRun` record itself reflects the failure instead of only logging/throwing into the void? This spec assumes no (see Out of Scope — pre-existing fire-and-forget characteristic), but flagging since it directly affects observability of the very failure mode this refactor is meant to make loud.

## Status: HAS_QUESTIONS
