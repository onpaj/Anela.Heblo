# Design: Remove decimal.MaxValue sentinel from CalculateForecastedDays

## Component Design

### PackingMaterial (Domain)
```csharp
/// Returns null when there is no consumption history to base a forecast on.
public decimal? CalculateForecastedDays(List<PackingMaterialLog> recentLogs)
{
    if (CurrentQuantity <= 0)
        return 0;

    var nonZeroConsumptions = recentLogs
        .Where(log => log.ChangeAmount < 0)
        .Select(log => Math.Abs(log.ChangeAmount))
        .Where(c => c > 0)
        .ToList();

    if (!nonZeroConsumptions.Any())
        return null; // No consumption history

    return CurrentQuantity / nonZeroConsumptions.Average();
}
```

### Handlers (Application)
Both handlers replace the sentinel check with null-propagation, keeping 1 dp rounding:
```csharp
var forecastedDays = material.CalculateForecastedDays(recentLogs);
var displayForecast = forecastedDays.HasValue
    ? Math.Round(forecastedDays.Value, 1)
    : (decimal?)null;
```
`GetPackingMaterialsListHandler` keeps its `withForecast`/`withoutForecast` counters driven by `displayForecast.HasValue`. `PackingMaterialMapper.ToDto(material, displayForecast)` unchanged.

### Tests
- New `PackingMaterialForecastTests` (domain): empty logs -> null; only positive/zero changes -> null; quantity 0 with empty logs -> 0; negative logs -> quantity / average magnitude.
- Update-quantity handler: no history -> `Material.ForecastedDays` null; with history -> rounded to 1 dp.
- Existing list handler and mapper tests unchanged and must pass.

## Data Schemas
No database, API or event schema changes. `PackingMaterialDto.ForecastedDays` remains `decimal?` (null / 0 / value rounded to 1 dp).
