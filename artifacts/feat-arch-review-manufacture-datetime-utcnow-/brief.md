## Module
Manufacture

## Finding
Three handlers inject `TimeProvider` and use it correctly for some fields, then fall back to `DateTime.UtcNow` for others in the same method body:

| File | Line(s) | Field(s) bypassed |
|------|---------|-------------------|
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs` | 46, 52 | `CreatedDate`, `StateChangedAt` |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs` | 47, 52 | `CreatedDate`, `StateChangedAt` |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/UpdateManufactureOrderHandler.cs` | 145 | note `CreatedAt` |

In all three cases `_timeProvider` is already injected and is used correctly in the same method (e.g. `CreateManufactureOrderHandler` uses it on lines 62–63 for `GetDefaultExpiration`/`GetDefaultLot`; `UpdateManufactureOrderHandler` uses it on lines 49, 55, 61 for ERP doc dates).

## Why it matters
Tests that pass a `FakeTimeProvider` into these handlers to freeze time will find that expiration, lot, and ERP-date fields are controlled, but `CreatedDate`, `StateChangedAt`, and note `CreatedAt` always reflect real wall-clock time. The inconsistency produces silent holes in time-sensitive test assertions — or forces tests to skip those fields entirely — without any hint that the handler has a split dependency.

## Suggested fix
Replace the five `DateTime.UtcNow` occurrences with `_timeProvider.GetUtcNow().DateTime`:

```csharp
// CreateManufactureOrderHandler.cs lines 46, 52
CreatedDate = _timeProvider.GetUtcNow().DateTime,
StateChangedAt = _timeProvider.GetUtcNow().DateTime,

// DuplicateManufactureOrderHandler.cs lines 47, 52
CreatedDate = _timeProvider.GetUtcNow().DateTime,
StateChangedAt = _timeProvider.GetUtcNow().DateTime,

// UpdateManufactureOrderHandler.cs line 145
CreatedAt = _timeProvider.GetUtcNow().DateTime,
```

No DI changes needed — `TimeProvider` is already registered and injected in all three handlers.

---
_Filed by daily arch-review routine on 2026-06-06._