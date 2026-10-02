# Specification: Polymorphic Result Shaping for GetDqtRunDetailHandler

## Summary
`GetDqtRunDetailHandler` currently dispatches on `DqtTestType` with two sequential `if` blocks (and a `throw new NotSupportedException` fallback) to decide how to shape the response for a DQT run's detail view. This violates the Open/Closed Principle: every new `DqtTestType` requires editing this handler. The module already solves the equivalent problem for *running* a test via `IDqtJobRunner.CanHandle()` polymorphic dispatch (see `RunDqtHandler`). This change introduces an equivalent extension point — an `IDqtResultShaper` abstraction — for the *result-shaping* concern, so each test-type family (invoice comparison, drift-based comparisons) owns its own shaping logic and the handler no longer branches on the enum.

## Background
`backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs` (lines 38–62) contains:
- An `if (run.TestType == DqtTestType.IssuedInvoiceComparison)` branch that maps `run.Results` to `InvoiceDqtResultDto`.
- An `if (run.TestType is DqtTestType.ProductPairing or DqtTestType.StockWriteBackReconciliation or DqtTestType.LotSumVsErpStock or DqtTestType.PriceComparison)` branch that loads paginated drift results via `_repository.GetDriftResultsAsync(...)` and maps them to `DqtDriftResultDto`.
- A `throw new NotSupportedException(...)` for any `DqtTestType` not covered by the two branches above (currently unreachable given the 5 existing enum values, but it is the smell the finding calls out: the list must be kept in sync by hand for every new type).

The module already has a working precedent for this exact shape of problem. `IDqtJobRunner.CanHandle(DqtTestType)` lets each runner (`InvoiceDqtJobRunner`, `DriftDqtJobRunner`) declare which types it owns, and `RunDqtHandler` / the DI container resolve the right runner via `.Single(r => r.CanHandle(...))` without knowing about concrete `DqtTestType` values. This spec extends that same pattern to the detail-retrieval path.

Constraints found while investigating the code (binding on the design):
- `IDqtRunRepository.GetWithResultsAsync(id, page, pageSize, ct)` returns the `DqtRun` aggregate with `Results` (the invoice-comparison child collection) already populated/paginated; it is used unconditionally today, for every test type, before the branch even runs.
- `IDqtRunRepository.GetDriftResultsAsync(runId, page, pageSize, ct)` is a **separate** query, only invoked for the drift-family branch.
- `GetDqtRunDetailResponse` (in `Contracts`) has four result-shape fields today: `Run`, `Results` (`List<InvoiceDqtResultDto>`), `DriftResults` (`List<DqtDriftResultDto>?`), `TotalDriftResults` (`int`). This DTO shape is a public contract (OpenAPI-generated TypeScript client consumes it) and **must not change** as part of this refactor — only the internal dispatch mechanism changes.
- `InvoiceDqtJobRunner` and `DriftDqtJobRunner` are the two existing types that parallel the two `if` branches; `DriftDqtJobRunner.CanHandle` in turn delegates to its injected `IEnumerable<IDriftDqtComparer>` (one per drift test type), so it already aggregately "knows" all 4 drift types without an enum switch.
- Both runners are registered in `DataQualityModule.AddDataQualityModule()` as `IDqtJobRunner` (`services.AddScoped<IDqtJobRunner, InvoiceDqtJobRunner>()` / `..., DriftDqtJobRunner>()`), alongside their narrower marker interfaces `IInvoiceDqtJobRunner` / `IDriftDqtJobRunner` used elsewhere.

## Functional Requirements

### FR-1: Introduce an `IDqtResultShaper` extension point
Add an interface (exact home determined by the architecture phase — likely `Anela.Heblo.Application.Features.DataQuality.Services`) with:
- `bool CanHandle(DqtTestType testType)` — mirrors `IDqtJobRunner.CanHandle` so the resolution pattern used by `RunDqtHandler` is reused verbatim.
- An async method that, given the loaded `DqtRun` and pagination parameters, returns the *result portion* of `GetDqtRunDetailResponse` (i.e., populates `Results`/`DriftResults`/`TotalDriftResults` on an existing response instance, or returns a small value object the handler assigns onto the response — implementation detail for architecture/design to decide, but the interface must not require the shaper to construct `Run` or `Success`/`ErrorCode`, since those are handler-owned).

**Acceptance criteria:**
- The interface lives in the `Anela.Heblo.Application.Features.DataQuality` namespace tree (module-internal, not a cross-module abstraction).
- The interface signature allows a shaper to perform its own data access (e.g., calling `_repository.GetDriftResultsAsync` or reusing the already-loaded `run.Results`) without the handler needing to branch on `TestType` to decide which repository method to call.

### FR-2: Implement the shaper for the invoice-comparison family
`InvoiceDqtJobRunner` (or a new sibling type, per architecture's judgment) implements `IDqtResultShaper` such that `CanHandle(DqtTestType.IssuedInvoiceComparison)` returns `true` and its shaping method returns `Results = _mapper.Map<List<InvoiceDqtResultDto>>(run.Results)` — identical output to today's first `if` branch, including that no extra repository call is made (the run's `Results` navigation is already loaded by `GetWithResultsAsync`).

**Acceptance criteria:**
- For a run with `TestType == IssuedInvoiceComparison`, `GetDqtRunDetailResponse.Results` is populated exactly as before; `DriftResults` is null/empty and `TotalDriftResults` is 0, matching current behavior (the old code path never touched those fields for this branch).

### FR-3: Implement the shaper for the drift family
`DriftDqtJobRunner` (or a new sibling type) implements `IDqtResultShaper` such that `CanHandle` returns `true` for exactly `ProductPairing`, `StockWriteBackReconciliation`, `LotSumVsErpStock`, and `PriceComparison` (reusing its existing `_comparers.Any(c => c.TestType == testType)` logic — no hardcoded enum list to duplicate), and its shaping method calls `_repository.GetDriftResultsAsync(run.Id, page, pageSize, ct)` and maps to `DriftResults` / `TotalDriftResults` exactly as today's second `if` branch.

**Acceptance criteria:**
- For each of the 4 drift test types, `GetDqtRunDetailResponse.DriftResults` and `TotalDriftResults` are populated exactly as before; `Results` stays empty (default `new()`), matching current behavior.
- No new hardcoded `DqtTestType` enumeration is introduced for this branch — `CanHandle` must derive its answer from the injected `IEnumerable<IDriftDqtComparer>`, exactly as `DriftDqtJobRunner.CanHandle` already does for the run-time dispatch path.

### FR-4: Rewrite `GetDqtRunDetailHandler` to delegate via `CanHandle`
Replace the two `if` blocks and the trailing `throw new NotSupportedException` with resolution of the matching `IDqtResultShaper` from an injected `IEnumerable<IDqtResultShaper>`, mirroring the resolution style used in `RunDqtHandler`/`DriftDqtJobRunner` (`.SingleOrDefault(s => s.CanHandle(run.TestType))`).

**Acceptance criteria:**
- When no shaper's `CanHandle` matches `run.TestType`, the handler returns `Success = false, ErrorCode = ErrorCodes.DqtUnsupportedTestType` — same externally-observable outcome as today (today this path is reached via the caught `NotSupportedException`; the rewritten handler may reach the same outcome without throwing, since a "no match" is an expected, not exceptional, condition once the check is a lookup rather than an unreachable code path). Preserving the same `ErrorCode` value is mandatory; whether it still goes through an exception is left to architecture/design.
- `run == null` handling (lines 29–36) is unchanged.
- The outer `try/catch` and its `ErrorCodes.Exception` fallback for genuine unexpected errors are preserved.

### FR-5: Register the new shaper(s) in `DataQualityModule`
Add `services.AddScoped<IDqtResultShaper, ...>()` registrations for each concrete shaper, alongside the existing `IDqtJobRunner` registrations.

**Acceptance criteria:**
- `dotnet build` succeeds; DI resolves `IEnumerable<IDqtResultShaper>` in `GetDqtRunDetailHandler` with exactly the shapers registered.

## Non-Functional Requirements

### NFR-1: Behavioral parity
This is a pure refactor of dispatch mechanism. No change to `GetDqtRunDetailRequest`, `GetDqtRunDetailResponse`, the OpenAPI contract, the generated TypeScript client, pagination semantics, or the mapping/DTO shapes is in scope. Existing tests covering `GetDqtRunDetailHandler` must continue to pass unmodified in behavior (test code may be updated only to reflect the new internal collaborators being mocked, e.g. injecting `IEnumerable<IDqtResultShaper>` instead of relying on the `if` chain).

### NFR-2: Extensibility (the actual point of the fix)
Adding a 6th `DqtTestType` in the future must require: (a) a new `IDqtResultShaper` implementation (or extending an existing drift-family shaper's `CanHandle`), and (b) one DI registration line — and must require **zero** changes to `GetDqtRunDetailHandler`.

## Data Model
No changes. `DqtRun`, `DqtTestType`, `InvoiceDqtResultDto`, `DqtDriftResultDto`, `GetDqtRunDetailResponse` are all unchanged in shape. Only a new interface `IDqtResultShaper` and its implementations are added.

## API / Interface Design
No HTTP/API contract changes — `GetDqtRunDetailRequest`/`Response` and the underlying `GetDqtRunDetail` endpoint are unaffected externally. Internal interface added:

```csharp
public interface IDqtResultShaper
{
    bool CanHandle(DqtTestType testType);
    // exact method signature/return shape left to architecture & design phases —
    // see FR-1 for the constraint that it must not own Run/Success/ErrorCode.
}
```

## Dependencies
- `IDqtRunRepository` (existing) — no interface changes required; shapers call its existing methods (`GetDriftResultsAsync`), the handler still calls `GetWithResultsAsync` once up front (unchanged) since both shaper families need the loaded `run`.
- `IMapper` (AutoMapper) — existing mapping profiles for `InvoiceDqtResultDto` / `DqtDriftResultDto` are reused unchanged.
- `ErrorCodes.DqtUnsupportedTestType` (existing) — reused for the "no shaper matched" case.

## Out of Scope
- Any change to how DQT runs are *executed* (`RunDqtHandler`, `IDqtJobRunner`) — that path already has the polymorphic pattern and is not touched, except that architecture may choose to have the same concrete types (`InvoiceDqtJobRunner`, `DriftDqtJobRunner`) implement both `IDqtJobRunner` and `IDqtResultShaper`, or may choose separate types — that decision is explicitly deferred to the architecture phase.
- Any change to `GetDqtRuns` (the list endpoint) or other DataQuality use cases.
- Any change to the frontend consuming `GetDqtRunDetailResponse`.
- Adding a 6th `DqtTestType` — this spec only builds the extension point, it does not add a new test type.

## Open Questions
None.

## Status: COMPLETE
