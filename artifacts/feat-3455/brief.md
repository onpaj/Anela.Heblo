# [arch-review] DataQuality: RunDqtHandler and GetDqtRunDetailHandler hardcode binary dispatch on DqtTestType

## Module
DataQuality

## Finding
Two handlers contain an `if/else` branch that assumes all `DqtTestType` values are either `IssuedInvoiceComparison` or a drift-category type. Adding a third fundamentally different test type would mis-route silently at runtime.

**`RunDqtHandler`** (`backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs`, lines 49–58):
```csharp
if (request.TestType == DqtTestType.IssuedInvoiceComparison)
{
    var runner = scope.ServiceProvider.GetRequiredService<IIssuedInvoiceComparisonDqtJobRunner>();
    await runner.RunAsync(run.Id);
}
else
{
    var runner = scope.ServiceProvider.GetRequiredService<IDriftDqtJobRunner>();
    await runner.RunAsync(run.Id);
}
```

**`GetDqtRunDetailHandler`** (`backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs`, lines 38–57):
```csharp
if (run.TestType == DqtTestType.IssuedInvoiceComparison)
{
    return new GetDqtRunDetailResponse { Results = _mapper.Map<List<...>>(run.Results), ... };
}
// implied else → drift path
var (driftItems, driftTotal) = await _repository.GetDriftResultsAsync(...);
return new GetDqtRunDetailResponse { DriftResults = ..., ... };
```

Both `else` branches assume everything that is not `IssuedInvoiceComparison` is a drift-type run. `DriftDqtJobRunner` then resolves the specific comparer via `_comparers.SingleOrDefault(c => c.TestType == run.TestType)` — which throws `InvalidOperationException` at runtime if the new type has no registered comparer.

## Why it matters
Open/Closed principle: both handlers must be modified each time a new top-level run category is introduced. The `IDriftDqtComparer` registration pattern already handles intra-drift extensibility correctly (no handler changes needed for new drift types). The invoice ↔ drift split at the handler level is the remaining open/closed gap.

Concretely: a future `DqtTestType.SupplierAudit = 4` with its own runner would be silently routed to `IDriftDqtJobRunner`, which would throw at runtime with a misleading "No IDriftDqtComparer registered" message — a failure the type system cannot catch.

## Suggested fix
Introduce a `IDqtJobRunner` interface with a `DqtTestType TestType` discriminator (parallel to `IDriftDqtComparer`), register both the invoice and drift runners under it, and resolve in the handler by `SingleOrDefault`:

```csharp
// New shared interface
public interface IDqtJobRunner
{
    DqtTestType TestType { get; }
    Task RunAsync(Guid runId, CancellationToken ct = default);
}
```

```csharp
// RunDqtHandler — no more if/else
var runner = scope.ServiceProvider
    .GetServices<IDqtJobRunner>()
    .SingleOrDefault(r => r.TestType == run.TestType)
    ?? throw new InvalidOperationException($"No IDqtJobRunner for {run.TestType}");
await runner.RunAsync(run.Id);
```

For `GetDqtRunDetailHandler`, extract result-loading into a strategy per runner (or keep the two-path DTO shape but guard it with a descriptive `switch` expression with an explicit `default: throw` rather than an implicit else). The minimum fix is to add a `default: throw new NotSupportedException(run.TestType.ToString())` branch so unrecognized types fail fast and clearly instead of falling through to the wrong path.

---
_Filed by daily arch-review routine on 2026-07-01._
