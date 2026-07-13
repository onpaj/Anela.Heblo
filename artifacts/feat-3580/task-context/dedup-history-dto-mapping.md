### task: dedup-history-dto-mapping

**Goal:** Add `PurchaseOrderHistoryDto.FromDomain(PurchaseOrderHistory h)` as a static factory and replace the three duplicated inline `new PurchaseOrderHistoryDto { ... }` mapping blocks with calls to it, with zero behavior change.

**Files to change (all under `backend/src/Anela.Heblo.Application/Features/Purchase/`):**

1. `Contracts/PurchaseOrderHistoryDto.cs`
   Current content:
   ```csharp
   namespace Anela.Heblo.Application.Features.Purchase.Contracts;

   public class PurchaseOrderHistoryDto
   {
       public int Id { get; set; }
       public string Action { get; set; } = null!;
       public string? OldValue { get; set; }
       public string? NewValue { get; set; }
       public DateTime ChangedAt { get; set; }
       public string ChangedBy { get; set; } = null!;
   }
   ```
   Change to:
   ```csharp
   using Anela.Heblo.Domain.Features.Purchase;

   namespace Anela.Heblo.Application.Features.Purchase.Contracts;

   public class PurchaseOrderHistoryDto
   {
       public int Id { get; set; }
       public string Action { get; set; } = null!;
       public string? OldValue { get; set; }
       public string? NewValue { get; set; }
       public DateTime ChangedAt { get; set; }
       public string ChangedBy { get; set; } = null!;

       public static PurchaseOrderHistoryDto FromDomain(PurchaseOrderHistory h) =>
           new()
           {
               Id = h.Id,
               Action = h.Action,
               OldValue = h.OldValue,
               NewValue = h.NewValue,
               ChangedAt = h.ChangedAt,
               ChangedBy = h.ChangedBy
           };
   }
   ```
   This mirrors `Contracts/PurchaseOrderLineDto.cs`'s `FromLine` factory (same file, same folder) — match its style exactly (single-expression static factory, object initializer, same brace/indentation conventions already in this file).

2. `UseCases/CreatePurchaseOrder/CreatePurchaseOrderHandler.cs` — inside the private `MapToResponse` method, currently at lines 114–122:
   ```csharp
   var history = purchaseOrder.History.Select(h => new PurchaseOrderHistoryDto
   {
       Id = h.Id,
       Action = h.Action,
       OldValue = h.OldValue,
       NewValue = h.NewValue,
       ChangedAt = h.ChangedAt,
       ChangedBy = h.ChangedBy
   }).ToList();
   ```
   Replace with:
   ```csharp
   var history = purchaseOrder.History.Select(PurchaseOrderHistoryDto.FromDomain).ToList();
   ```
   Do not touch anything else in `MapToResponse` (the `lines` assignment above it and the `CreatePurchaseOrderResponse` construction below it stay exactly as-is).

3. `UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdHandler.cs` — inside the `GetPurchaseOrderByIdResponse` object initializer, currently at lines 71–79:
   ```csharp
   History = purchaseOrder.History.Select(h => new PurchaseOrderHistoryDto
   {
       Id = h.Id,
       Action = h.Action,
       OldValue = h.OldValue,
       NewValue = h.NewValue,
       ChangedAt = h.ChangedAt,
       ChangedBy = h.ChangedBy
   }).OrderByDescending(h => h.ChangedAt).ToList(),
   ```
   Replace with:
   ```csharp
   History = purchaseOrder.History.Select(PurchaseOrderHistoryDto.FromDomain).OrderByDescending(h => h.ChangedAt).ToList(),
   ```
   Critical: keep `.OrderByDescending(h => h.ChangedAt)` immediately after `.Select(...)`, in that order, operating on the DTO's `ChangedAt` — do not reorder relative to `Select`, drop it, or move it before the mapping. This is called out explicitly as a risk in the arch review.

4. `UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryHandler.cs` — currently at lines 37–47:
   ```csharp
   var items = history
       .Select(h => new PurchaseOrderHistoryDto
       {
           Id = h.Id,
           Action = h.Action,
           OldValue = h.OldValue,
           NewValue = h.NewValue,
           ChangedAt = h.ChangedAt,
           ChangedBy = h.ChangedBy,
       })
       .ToList();
   ```
   Replace with:
   ```csharp
   var items = history
       .Select(PurchaseOrderHistoryDto.FromDomain)
       .ToList();
   ```

**Do not change:** method signatures, logging calls, response construction, ordering/assignment of any other field, or any file not listed above. `PurchaseOrderLineDto.cs` and `PurchaseOrderHistory.cs` (domain entity) are read-only references — do not modify them.

**Acceptance criteria:**
- `PurchaseOrderHistoryDto.FromDomain(PurchaseOrderHistory h)` exists as a `public static` single-expression factory, matching `PurchaseOrderLineDto.FromLine`'s style, mapping all six fields (`Id`, `Action`, `OldValue`, `NewValue`, `ChangedAt`, `ChangedBy`) 1:1 with no transformation.
- `grep -rn "new PurchaseOrderHistoryDto" backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/` returns no matches (the only remaining construction is inside `FromDomain` itself, in `Contracts/`).
- `GetPurchaseOrderByIdHandler`'s `.Select(...).OrderByDescending(h => h.ChangedAt).ToList()` sequence is preserved exactly (mapping first, ordering second).
- No other lines in the three handler files change (verify via `git diff` — only the mapping blocks and, in `PurchaseOrderHistoryDto.cs`, the added `using` and factory method should appear).

**Verification steps (run from the `backend/` directory unless noted):**
1. `grep -rn "new PurchaseOrderHistoryDto" ../backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/` (or equivalent path from repo root: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/`) — expect zero matches.
2. `dotnet build Anela.Heblo.sln` from the repo root (`/home/user/worktrees/feature-3580-Arch-Review-Purchase-Purchaseorderhistorydto-Mappi`) — must succeed with no new warnings/errors.
3. `dotnet format Anela.Heblo.sln` from the repo root — apply and ensure no unexpected formatting diffs beyond the intended edits.
4. Run the existing unit tests unmodified and confirm they pass:
   - `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CreatePurchaseOrderHandlerTests"`
   - `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetPurchaseOrderHistoryHandlerTests"`
   - Note: no dedicated `GetPurchaseOrderByIdHandlerTests.cs` currently exists in the repo (confirmed absent at plan time) — nothing to run for that handler beyond the build succeeding; do not add a new test file, per the spec's "Out of Scope" section (new tests for `FromDomain` are explicitly not required).
5. Do not modify any test file — the spec requires all existing tests to pass **unmodified**, since output shape/values are unchanged.

**Definition of done:** all four verification steps pass, `git diff` shows changes confined to the four files listed above, and no unrelated code (logging, response shape, other handlers) was touched.
