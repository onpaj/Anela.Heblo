## Module
Manufacture

## Finding
`ManufactureOrderRepository.GenerateOrderNumberAsync` uses `DateTime.Now` (local server time) to derive the year prefix for the order number:

```csharp
// backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs  line 150
var currentYear = DateTime.Now.Year;
var prefix = $"MO-{currentYear}-";
```

Every other time-stamped value in this module uses either `_timeProvider.GetUtcNow()` (handlers) or `DateTime.UtcNow` (even the handlers that the existing issues #2676–#2679 flag). `DateTime.Now` is the odd one out: it reads the OS local clock, which on most server deployments is UTC — but is not guaranteed, and behaves incorrectly on any host that runs with a non-UTC offset (e.g. CET = UTC+1).

Concrete failure scenario: at 23:30 UTC on 31 December the local time on a CET server is already 00:30 on 1 January. `GenerateOrderNumberAsync` will emit `MO-{next_year}-001` while the handler's `TimeProvider`-controlled timestamps still say the previous year. Conversely, on 31 December CET, an order submitted at 23:30 local time (= 22:30 UTC) carries a `MO-{next_year}` prefix while the order's `CreatedDate` column still says the previous year — creating an inconsistent audit record.

## Why it matters
- **Correctness**: year-based order numbering diverges from the UTC-based audit timestamps stored on the same row whenever local time ≠ UTC.
- **Infrastructure encapsulation**: the repository currently self-sources a temporal dependency (`DateTime.Now`) instead of accepting it from the application layer, which owns `TimeProvider`. This makes the function untestable for year-boundary edge cases.
- **Consistency**: all other date/time dependencies in the module flow through `TimeProvider` or at minimum `DateTime.UtcNow`; this is the only site using local wall-clock time.

## Suggested fix
Remove the temporal dependency from the repository entirely. Accept the year as a parameter from the caller (the handler already injects `TimeProvider`):

```csharp
// IManufactureOrderRepository (Domain layer)
Task<string> GenerateOrderNumberAsync(int year, CancellationToken cancellationToken = default);

// ManufactureOrderRepository (Persistence layer) — line 150
public async Task<string> GenerateOrderNumberAsync(int year, CancellationToken cancellationToken = default)
{
    var prefix = $"MO-{year}-";
    // ... rest unchanged
}

// CreateManufactureOrderHandler — after generating the order number
var year = _timeProvider.GetUtcNow().Year;
var orderNumber = await _repository.GenerateOrderNumberAsync(year, cancellationToken);

// DuplicateManufactureOrderHandler — same pattern
var year = _timeProvider.GetUtcNow().Year;
var orderNumber = await _repository.GenerateOrderNumberAsync(year, cancellationToken);
```

The interface change is small (adding one `int year` parameter), touches two callers (`CreateManufactureOrderHandler`, `DuplicateManufactureOrderHandler`), and removes a hidden temporal side-effect from the infrastructure layer.

---
_Filed by daily arch-review routine on 2026-06-06._