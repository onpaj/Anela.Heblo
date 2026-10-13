# Implementation: shrink-ieshoporderclient-interface

## What was implemented
Removed the 4 test-only lifecycle method declarations (`CreateOrderAsync`,
`DeleteOrderAsync`, `GetRecentOrdersAsync`, `ListByExternalCodePrefixAsync`)
from `IEshopOrderClient`, per the task context's exact replacement content.
Every remaining method, its doc comment, and ordering were left untouched.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` — removed the 4 relocated method declarations; interface now exposes only `GetOrderStatusIdAsync`, `UpdateStatusAsync`, `GetEshopRemarkAsync`, `UpdateEshopRemarkAsync`, `AppendEshopRemarkAsync`, `ListOrdersByStatusAsync`, `MarkAsPackedAsync`.

## Tests
No new tests required by this task; it is a pure interface trim.

## How to verify
1. `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` → `Build succeeded.`, 0 errors.
2. `cd backend && grep -rn "CreateOrderAsync\|DeleteOrderAsync\|GetRecentOrdersAsync\|ListByExternalCodePrefixAsync" src/Anela.Heblo.Application/` → no source matches (only stale `bin/` binaries match, which is expected and irrelevant).

## Notes
- Build succeeded with 0 errors, confirming FR-2's regression check ("zero Application-layer production callers of the 4 methods") held.
- Grep confirmed no remaining source references to the 4 removed methods anywhere under `src/Anela.Heblo.Application/`.
- No deviations from the task context.

## PR Summary
Shrinks `IEshopOrderClient` by removing 4 method declarations that are being relocated elsewhere per the arch-review plan for this issue (create/delete/list-recent/list-by-prefix order-lifecycle helpers used only by test/setup code, not production Application-layer logic).
