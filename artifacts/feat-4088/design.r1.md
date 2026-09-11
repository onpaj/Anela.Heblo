# Design: Convert `ConsumeInventoryResult` from a record to a class

## Component Design

No components are added, removed, or moved. This is a type-declaration change to a single existing Application-layer contract type, plus mechanical call-site updates to match.

### `ConsumeInventoryResult` (Logistics module, `Features/Logistics/Contracts/ConsumeInventoryResult.cs`)
- Responsibility: immutable outcome-carrying result returned by `IInventoryReservationService.TryConsumeAsync`, discriminating between success and the two failure modes.
- Changes from `sealed record ConsumeInventoryResult(ConsumeInventoryOutcome Outcome)` to `sealed class` with:
  - `public ConsumeInventoryOutcome Outcome { get; init; }`
  - `public static ConsumeInventoryResult Success()`
  - `public static ConsumeInventoryResult InventoryNotFound()`
  - `public static ConsumeInventoryResult InsufficientStock()`
- Each factory method returns a new instance with `Outcome` set to the matching `ConsumeInventoryOutcome` value. No public constructor is added; the record's positional-constructor call shape (`new ConsumeInventoryResult(outcome)`) is replaced by the factory calls at every call site.
- `ConsumeInventoryOutcome` enum (`Success`, `InventoryNotFound`, `InsufficientStock`) is unchanged.
- XML doc comment on the type is updated only to drop "Sealed record" wording (e.g. "Sealed class with an outcome discriminator...").

### `IInventoryReservationService` (Logistics module, module-boundary interface)
- `TryConsumeAsync(...)` signature is unchanged: `Task<ConsumeInventoryResult> TryConsumeAsync(int inventoryId, decimal amount, string userName, DateTime timestamp, int boxId, string? boxCode, bool allowNegativeStock, CancellationToken cancellationToken)`. Not edited.

### `ManufactureInventoryReservationAdapter` (Manufacture module, implements the Logistics contract)
- `TryConsumeAsync` — its three construction sites (item-not-found path, `InsufficientStock` path from `item.Consume` throwing `InvalidOperationException`, and the success path) switch from `new ConsumeInventoryResult(ConsumeInventoryOutcome.X)` to `ConsumeInventoryResult.X()`. Branching logic and exception handling are unchanged.

### `AddItemToBoxHandler` (Logistics module, consumer)
- Only reads `consumeResult.Outcome` via `switch`. Not edited — property access is identical on a class.

### Test doubles
- `AddItemToBoxHandlerTests.cs` — three `.ReturnsAsync(new ConsumeInventoryResult(ConsumeInventoryOutcome.X))` setups switch to `.ReturnsAsync(ConsumeInventoryResult.X())`. Assertions unchanged.
- `ManufactureInventoryReservationAdapterTests.cs` — only reads `result.Outcome`. Not edited.

## Data Schemas

`ConsumeInventoryResult` is an in-memory Application-layer contract — not persisted, not serialized, never returned from a controller, and outside the NSwag/OpenAPI surface. No database schema, API request/response shape, or event payload is affected by this change.

Public shape after the change:

```csharp
public sealed class ConsumeInventoryResult
{
    public ConsumeInventoryOutcome Outcome { get; init; }

    public static ConsumeInventoryResult Success() => new() { Outcome = ConsumeInventoryOutcome.Success };
    public static ConsumeInventoryResult InventoryNotFound() => new() { Outcome = ConsumeInventoryOutcome.InventoryNotFound };
    public static ConsumeInventoryResult InsufficientStock() => new() { Outcome = ConsumeInventoryOutcome.InsufficientStock };
}
```

`ConsumeInventoryOutcome` enum (unchanged): `Success`, `InventoryNotFound`, `InsufficientStock`.
