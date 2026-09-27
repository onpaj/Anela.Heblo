# Design: Polymorphic Result Shaping for GetDqtRunDetailHandler

## Component Design

### `IDqtResultShaper` (new interface)
Location: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtResultShaper.cs`

Responsibility: declare whether an implementation owns the result-shaping for a given `DqtTestType` (`CanHandle`), and, when it does, populate the result portion of an already-partially-built `GetDqtRunDetailResponse` (`ShapeAsync`). Implementations must not set `Run`, `Success`, or `ErrorCode` — those remain owned exclusively by `GetDqtRunDetailHandler`.

```csharp
public interface IDqtResultShaper
{
    bool CanHandle(DqtTestType testType);

    Task ShapeAsync(
        DqtRun run,
        GetDqtRunDetailResponse response,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
```

### `InvoiceDqtResultShaper` (new class)
Location: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtResultShaper.cs`

Responsibility: owns result-shaping for `DqtTestType.IssuedInvoiceComparison`. Depends only on `IMapper`. `CanHandle` returns `testType == DqtTestType.IssuedInvoiceComparison`. `ShapeAsync` sets `response.Results = _mapper.Map<List<InvoiceDqtResultDto>>(run.Results)` and returns `Task.CompletedTask` — no repository call, since `run.Results` is already loaded by the handler's prior `GetWithResultsAsync` call.

### `DriftDqtResultShaper` (new class)
Location: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtResultShaper.cs`

Responsibility: owns result-shaping for every drift-family `DqtTestType` (currently `ProductPairing`, `StockWriteBackReconciliation`, `LotSumVsErpStock`, `PriceComparison`). Depends on `IDqtRunRepository`, `IEnumerable<IDriftDqtComparer>`, `IMapper`. `CanHandle(testType)` returns `_comparers.Any(c => c.TestType == testType)` — identical derivation to `DriftDqtJobRunner.CanHandle`, so the 4-type list is never hardcoded here. `ShapeAsync` calls `_repository.GetDriftResultsAsync(run.Id, page, pageSize, ct)`, then sets `response.DriftResults = _mapper.Map<List<DqtDriftResultDto>>(driftItems)` and `response.TotalDriftResults = driftTotal`.

### `GetDqtRunDetailHandler` (existing class, edited)
Location: `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs`

Responsibility unchanged (load a run and return its shaped detail), but the dispatch mechanism changes:
- Constructor gains `IEnumerable<IDqtResultShaper> shapers` (alongside the existing `IDqtRunRepository`, `IMapper`, `ILogger<GetDqtRunDetailHandler>`).
- After the existing `run == null` check, the two `if` blocks and the trailing `throw new NotSupportedException(...)` are replaced by:
  - `var shaper = _shapers.SingleOrDefault(s => s.CanHandle(run.TestType));`
  - If `shaper == null` → return `Success = false, ErrorCode = ErrorCodes.DqtUnsupportedTestType` (no `Run` set — matches today's behavior where `Run` stays `null` on the unsupported-type path).
  - Otherwise → build `response` with `Success = true, Run = _mapper.Map<DqtRunDto>(run)`, call `await shaper.ShapeAsync(run, response, request.ResultPage, request.ResultPageSize, cancellationToken)`, return `response`.
- The outer `try/catch(Exception ex)` and its `ErrorCode = ex is NotSupportedException ? ErrorCodes.DqtUnsupportedTestType : ErrorCodes.Exception` ternary are left in place unchanged (defensive fallback; no code path in the rewritten handler throws `NotSupportedException` for the "no shaper" case anymore, but the ternary is harmless to keep and is out of scope to remove).

### `DataQualityModule` (existing class, edited)
Location: `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs`

Two new lines added directly under the existing `IDqtJobRunner` registrations:
```csharp
services.AddScoped<IDqtResultShaper, InvoiceDqtResultShaper>();
services.AddScoped<IDqtResultShaper, DriftDqtResultShaper>();
```

## Data Schemas

No schema changes. No database migrations. No API contract changes.

- `GetDqtRunDetailRequest` — unchanged (`Id`, `ResultPage`, `ResultPageSize`).
- `GetDqtRunDetailResponse` — unchanged shape (`Run`, `Results`, `DriftResults`, `TotalDriftResults`, plus inherited `Success`/`ErrorCode` from `BaseResponse`). Only *which internal component writes which field* changes, not the DTO itself, so the OpenAPI-generated TypeScript client is unaffected.
- `IDqtResultShaper.ShapeAsync` is a new internal (non-API) method signature; it is not exposed through any controller, MediatR request, or OpenAPI contract — it is a module-internal collaborator between `GetDqtRunDetailHandler` and its two new shaper classes, so the "DTOs are classes, never records" contract rule does not apply here (no new DTO type is introduced at all — the existing `GetDqtRunDetailResponse` class is reused as the shaping target).
