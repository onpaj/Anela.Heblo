## Module
Logistics

## Finding
Two Logistics handlers directly inject `IManufacturedProductInventoryRepository` from the Manufacture module's domain, bypassing the required contract-based cross-module communication pattern:

- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/AddItemToBox/AddItemToBoxHandler.cs` — line 16: `private readonly IManufacturedProductInventoryRepository _inventoryRepository;`
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` — line 17: `private readonly IManufacturedProductInventoryRepository _inventoryRepository;`

Both handlers call `_inventoryRepository.GetByIdAsync(...)` and `_inventoryRepository.UpdateAsync(...)` on Manufacture-owned inventory items, and `AddItemToBoxHandler` calls `inventoryItem.Consume(...)` — a Manufacture domain method — directly from the Logistics application layer.

## Why it matters
`development_guidelines.md` explicitly forbids "Direct access to another module's entities" and "Shared repositories across modules." This creates tight coupling: any change to `ManufacturedProductInventoryItem` or `IManufacturedProductInventoryRepository` can break Logistics handlers. It also blocks the future path to independent module deployment.

## Suggested fix
Apply the cross-module consumer/provider contract pattern documented in `development_guidelines.md`:

1. Define a Logistics-owned interface in `Application/Features/Logistics/Contracts/`, e.g.:
   ```csharp
   public interface IInventoryReservationService
   {
       Task<bool> TryConsumeAsync(int inventoryId, decimal amount, string userName, DateTime timestamp, int boxId, string? boxCode, bool allowNegative, CancellationToken ct);
       Task RestoreAsync(int inventoryId, decimal amount, string userName, DateTime timestamp, int boxId, string? boxCode, CancellationToken ct);
   }
   ```
2. Have Manufacture implement an adapter in `Application/Features/Manufacture/Infrastructure/` and register the binding in `ManufactureModule`.
3. Replace the direct repository injection in both handlers with the new interface.

---
_Filed by daily arch-review routine on 2026-05-15._