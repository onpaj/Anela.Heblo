# Design: Extract PackingMaterialDto mapping into a shared mapper

## Component Design

### `PackingMaterialMapper` (new)
- **Location:** `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Mapping/PackingMaterialMapper.cs`
- **Visibility:** `internal static class`
- **Responsibility:** Pure, side-effect-free mapping from a `PackingMaterial` domain entity plus an already-computed forecast value to a `PackingMaterialDto`. Owns exactly one thing: the field-by-field copy (including the `ConsumptionTypeText` derivation via the existing `PackingMaterialsTextHelper`). Owns nothing else — it does not read the repository, does not compute forecasts, does not log.
- **Interface:**
  ```csharp
  public static PackingMaterialDto ToDto(PackingMaterial material, decimal? forecastedDays)
  ```
- **Collaborators:** calls `PackingMaterialsTextHelper.ConsumptionTypeText(ConsumptionType)` (existing, unchanged, `internal static`, same assembly).

### Call sites (modified, behavior unchanged)
Each of the four handlers keeps its own existing logic for *what* forecast value to pass, and delegates only the DTO construction to the mapper:

| Handler | Forecast value passed | Existing logic preserved |
|---|---|---|
| `CreatePackingMaterialHandler` | `null` | No repository read for logs — new material has no history |
| `UpdatePackingMaterialHandler` | `null` | Unchanged — this handler has never computed a forecast |
| `UpdatePackingMaterialQuantityHandler` | `displayForecast` | Recent-logs fetch, `CalculateForecastedDays`, `decimal.MaxValue` → `null` guard, `Math.Round(..., 1)` — all stay exactly where they are today, upstream of the mapper call |
| `GetPackingMaterialsListHandler` | `displayForecast` (per item, inside the existing `.Select(...)`) | Batch log fetch, per-item `CalculateForecastedDays`, `withForecast`/`withoutForecast`/`totalLogs` counters feeding the existing `LogDebug` call — unaffected, since these are computed before the mapper call and do not depend on the DTO |

No component's public interface (constructor dependencies, MediatR request/response shape, controller route) changes. `PackingMaterialMapper` has no constructor dependencies to inject — it is a stateless static class, consistent with `JournalEntryMapper`.

## Data Schemas

No schema changes. For completeness, the (unchanged) shape being produced:

```csharp
public class PackingMaterialDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public decimal ConsumptionRate { get; set; }
    public ConsumptionType ConsumptionType { get; set; }
    public string ConsumptionTypeText { get; set; } = null!;
    public decimal CurrentQuantity { get; set; }
    public decimal? ForecastedDays { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

No database migration, no API request/response contract change, no event payload change. The generated OpenAPI TypeScript client is unaffected since the wire shape of every endpoint that returns `PackingMaterialDto` is identical before and after this change.
