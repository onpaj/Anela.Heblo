# Implementation: dedup-history-dto-mapping

## What was implemented
Added a static factory `PurchaseOrderHistoryDto.FromDomain(PurchaseOrderHistory h)` mirroring the existing `PurchaseOrderLineDto.FromLine` pattern, and replaced the three duplicated inline `new PurchaseOrderHistoryDto { ... }` mapping blocks in the Purchase handlers with calls to it. No behavior change — all six fields (`Id`, `Action`, `OldValue`, `NewValue`, `ChangedAt`, `ChangedBy`) map 1:1 as before, and the `.OrderByDescending(h => h.ChangedAt)` step in `GetPurchaseOrderByIdHandler` is preserved immediately after `.Select(...)`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchaseOrderHistoryDto.cs` — added `using Anela.Heblo.Domain.Features.Purchase;` and the `FromDomain` static factory.
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderHandler.cs` — replaced inline mapping with `.Select(PurchaseOrderHistoryDto.FromDomain)`.
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdHandler.cs` — replaced inline mapping with `.Select(PurchaseOrderHistoryDto.FromDomain).OrderByDescending(h => h.ChangedAt)`.
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryHandler.cs` — replaced inline mapping with `.Select(PurchaseOrderHistoryDto.FromDomain)`.

## Tests
No new tests required (per spec's Out of Scope — pure mechanical refactor, output shape unchanged). Ran existing suites unmodified:
- `CreatePurchaseOrderHandlerTests` — 10/10 passed.
- `GetPurchaseOrderHistoryHandlerTests` — 4/4 passed.
No dedicated `GetPurchaseOrderByIdHandlerTests.cs` exists in the repo.

## How to verify
1. `grep -rn "new PurchaseOrderHistoryDto" backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/` — zero matches.
2. `dotnet build Anela.Heblo.sln` — succeeds (0 errors).
3. `dotnet format Anela.Heblo.sln` — no additional diff.
4. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CreatePurchaseOrderHandlerTests"` and `--filter "FullyQualifiedName~GetPurchaseOrderHistoryHandlerTests"` — both pass.

## Notes
None. Change is confined exactly to the four files named in the task plan; `PurchaseOrderLineDto.cs` and the domain entity were not touched.

## PR Summary
Deduplicated the `PurchaseOrderHistory` → `PurchaseOrderHistoryDto` mapping that was copy-pasted verbatim across three handlers by adding a static `PurchaseOrderHistoryDto.FromDomain` factory, mirroring the existing `PurchaseOrderLineDto.FromLine` convention. All three call sites now delegate to the single factory; behavior and output are unchanged.

### Changes
- `Contracts/PurchaseOrderHistoryDto.cs` — added `FromDomain` static factory.
- `UseCases/CreatePurchaseOrder/CreatePurchaseOrderHandler.cs`, `UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdHandler.cs`, `UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryHandler.cs` — replaced inline mapping with `.Select(PurchaseOrderHistoryDto.FromDomain)`.

## Status
DONE
