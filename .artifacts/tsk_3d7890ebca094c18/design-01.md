# Design — GetWarehouseStatisticsHandler: TimeProvider + hardcoded capacity constant

No UI section — this is a backend-only refactor of a MediatR handler's internals. No API contract, request/response shape, or frontend behavior changes.

## Component design

### 1. `CatalogConstants` (existing static class)

Add one new field, following the existing style (public const, no `_` prefix, XML doc comment matching the two existing constants):

```csharp
/// <summary>
/// Warehouse physical capacity in kilograms, used to compute WarehouseUtilizationPercentage
/// in GetWarehouseStatisticsHandler. Adjust here if the physical warehouse capacity changes.
/// </summary>
public const double WarehouseCapacityKg = 3000.0;
```

Placement: append after `HISTORY_FLOOR_DATE`. Naming: `WarehouseCapacityKg` (PascalCase) — the two existing constants use `ALL_CAPS_WITH_UNDERSCORES`, but that's inconsistent with standard C# const style already in this codebase's other constants classes; the finding's suggested fix uses PascalCase and that's what we'll follow (matches broader codebase convention for constants outside this one file — not introducing a third style, just not propagating the SCREAMING_CASE one further).

### 2. `GetWarehouseStatisticsHandler`

Responsibility is unchanged: aggregate catalog items into warehouse statistics. Only its dependencies and two internal expressions change.

```csharp
public class GetWarehouseStatisticsHandler : IRequestHandler<GetWarehouseStatisticsRequest, GetWarehouseStatisticsResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly TimeProvider _timeProvider;

    public GetWarehouseStatisticsHandler(ICatalogRepository catalogRepository, TimeProvider timeProvider)
    {
        _catalogRepository = catalogRepository;
        _timeProvider = timeProvider;
    }

    public async Task<GetWarehouseStatisticsResponse> Handle(GetWarehouseStatisticsRequest request, CancellationToken cancellationToken)
    {
        ... // unchanged aggregation logic

        var utilizationPercentage = CatalogConstants.WarehouseCapacityKg > 0
            ? (totalWeight / CatalogConstants.WarehouseCapacityKg) * 100
            : 0;

        ...

        return new GetWarehouseStatisticsResponse
        {
            TotalQuantity = totalQuantity,
            TotalWeight = totalWeight,
            WarehouseCapacityKg = CatalogConstants.WarehouseCapacityKg,
            WarehouseUtilizationPercentage = utilizationPercentage,
            TotalProductCount = totalProductCount,
            LastUpdated = _timeProvider.GetUtcNow().UtcDateTime
        };
    }
}
```

This constructor signature (`ICatalogRepository`, then `TimeProvider`) mirrors the parameter ordering convention in `GetCatalogDetailHandler` and `GetProductMarginsHandler` (repository/mapper-type dependencies first, `TimeProvider` next, logger last — this handler has no `IMapper` or `ILogger`, so it's just the two params).

`.UtcDateTime` (not `.Date` or `.DateTime`) is the correct conversion: `GetWarehouseStatisticsResponse.LastUpdated` is `DateTime` (not `DateTimeOffset`), and `.UtcDateTime` preserves UTC `Kind` — `.DateTime` (used by `GetProductMarginsHandler`) returns `Kind=Unspecified`, `.Date` truncates to midnight. Neither sibling handler's choice is directly reusable here; `.UtcDateTime` is correct for this field's semantics.

No DI registration change needed — `TimeProvider` is already a container singleton (used by the two sibling handlers), and MediatR handler resolution auto-injects it.

### 3. Test coverage (new file)

No test currently targets this handler. Add `backend/test/Anela.Heblo.Tests/Features/Catalog/GetWarehouseStatisticsHandlerTests.cs`, following the exact mocking pattern already used in the sibling `GetCatalogDetailHandlerTests.cs`:

- `Mock<ICatalogRepository>` returning a small fixed list of catalog items (mix of `Product`/`Goods` types, some with `GrossWeight`, some without, to exercise the weight-sum filter).
- `Mock<TimeProvider>` (not `Microsoft.Extensions.Time.Testing.FakeTimeProvider` — the codebase's established pattern here is `Mock<TimeProvider>` with `.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(currentDate))`, exactly as in `GetCatalogDetailHandlerTests.cs`).
- Assertions:
  - `LastUpdated` equals the mocked instant exactly (no time-window tolerance) — this is the concrete proof that the finding's stated benefit ("tests cannot assert on the field without accepting a time-window race condition") is resolved.
  - `WarehouseCapacityKg` in the response equals `CatalogConstants.WarehouseCapacityKg`.
  - `WarehouseUtilizationPercentage` matches the existing formula given the fixture's known total weight (regression guard that the refactor didn't change behavior).

This is the one net-new test file in this change; everything else is a same-shape edit to existing files.

## Data schemas

No schema, DTO, or wire-contract changes.

- `GetWarehouseStatisticsRequest` / `GetWarehouseStatisticsResponse` — unchanged field set, unchanged types, unchanged HTTP/JSON shape.
- Note (informational, not part of this change): `GetWarehouseStatisticsResponse.WarehouseCapacityKg` currently has a C# property initializer default of `8500`, which is always overwritten by the handler before the response leaves `Handle()`. That default is dead code independent of this fix — out of scope per the plan's boundaries (finding is only about the handler's local `const`), left untouched. Mentioning it here so it isn't mistaken for something this change should also fix.
- `CatalogConstants.WarehouseCapacityKg` is a new internal constant, not exposed via any API — it only feeds `GetWarehouseStatisticsResponse.WarehouseCapacityKg` at runtime the same way the old local `const` did.

## Verification

- `dotnet build` and `dotnet format` (per repo validation rules).
- New unit test passes; run the Catalog-module test project (or full backend suite) to confirm no other file references the old two-argument-less constructor of `GetWarehouseStatisticsHandler` (repo-wide search in the previous step found none, including under `backend/test`).
