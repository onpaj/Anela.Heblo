## Module
PackingMaterials

## Finding
`GetPackingMaterialsListHandler` computes `ForecastedDays` by calling `material.CalculateForecastedDays(recentLogs)` (lines 26-32), but `recentLogs` is always empty because the repository method it uses does not load logs:

```csharp
// GetPackingMaterialsListHandler.cs:22-31
var materials = await _repository.GetAllWithLogsAsync(cancellationToken);
var oneMonthAgo = DateTime.UtcNow.AddMonths(-1);
var materialDtos = materials.Select(material =>
{
    var recentLogs = material.Logs               // always []
        .Where(log => log.CreatedAt >= oneMonthAgo)
        .ToList();
    var forecastedDays = material.CalculateForecastedDays(recentLogs); // always decimal.MaxValue
    var displayForecast = forecastedDays == decimal.MaxValue ? null : (decimal?)Math.Round(forecastedDays, 1);
    // displayForecast is always null
```

`PackingMaterialRepository.GetAllWithLogsAsync` (lines 14-19) does not include logs:

```csharp
// PackingMaterialRepository.cs:14-19
public async Task<IEnumerable<PackingMaterial>> GetAllWithLogsAsync(CancellationToken cancellationToken = default)
{
    // For now, return materials without logs since we removed the navigation property
    // We'll load logs separately when needed
    return await DbSet.ToListAsync(cancellationToken);
}
```

The EF configuration for `PackingMaterial` (`PackingMaterialConfiguration.cs`) has no `HasMany` relationship configured for logs, so even adding `.Include(pm => pm.Logs)` would not work — the navigation must be re-wired. `PackingMaterial._logs` is a private backing field initialized to an empty list; it is never populated through this code path.

The `GetRecentLogsAsync` repository method exists and correctly queries logs by materialId + date range — it is used in `UpdatePackingMaterialQuantityHandler` (line 40) but not in `GetPackingMaterialsListHandler`.

## Why it matters
- `ForecastedDays` is exposed in the API response and presumably displayed in the UI, but always returns `null`. Users receive no forecast for any material, with no indication that something is wrong.
- The method name `GetAllWithLogsAsync` promises log-loading but silently skips it — callers that depend on `material.Logs` will be broken by design.
- The comment "We'll load logs separately when needed" was apparently never followed up on for this handler.

## Suggested fix
In `GetPackingMaterialsListHandler`, load logs via the existing `GetRecentLogsAsync` method rather than relying on the navigation property:

```csharp
var oneMonthAgo = DateTime.UtcNow.AddMonths(-1);
var materials = (await _repository.GetAllAsync(cancellationToken)).ToList();
// Load logs per-material (or add a bulk GetRecentLogsByMaterialIdsAsync to avoid N+1)
var materialDtos = await Task.WhenAll(materials.Select(async material =>
{
    var recentLogs = (await _repository.GetRecentLogsAsync(material.Id, oneMonthAgo, cancellationToken)).ToList();
    var forecastedDays = material.CalculateForecastedDays(recentLogs);
    // ...
}));
```

For efficiency, extend `IPackingMaterialRepository` with a bulk log query (e.g. `GetRecentLogsForMaterialsAsync(IEnumerable<int> materialIds, DateTime from)`). Rename `GetAllWithLogsAsync` to `GetAllAsync` to reflect what it actually does.

---
_Filed by daily arch-review routine on 2026-05-20._