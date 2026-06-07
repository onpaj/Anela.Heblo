All 5 tasks complete. Writing the output summary.

---

# Implementation: Separate Internal `PackingOrderItem` from Public API DTO

## What was implemented

Split the dual-role `PackingOrderItem` class (formerly both an internal adapter contract and a public API DTO) into three clearly scoped types:
- Internal: `PackingOrderItem` (keeps `WeightGrams`) — adapter contract only
- Public: `PackingOrderItemDto` — GetPackingOrder use case
- Public: `ScanPackingOrderItemDto` — ScanPackingOrder use case

Both handlers now project at the response boundary (dropping `WeightGrams`). The weight calculation in `ScanPackingOrderHandler:102` continues reading from `order.Items` (internal collection). Reflection-based tests lock the API surface so future field leakage fails CI.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs` — doc comment tightened: "Internal contract — not an API DTO"
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/PackingOrderItemDto.cs` — new `sealed class`, 4 fields (Name, Quantity, ImageUrl, SetName)
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderResponse.cs` — `Items` type changed to `List<PackingOrderItemDto>`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs` — `Items` assignment replaced with `.Select()` projection
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderItemDto.cs` — new `sealed class`, 4 fields
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderResponse.cs` — `ScanOrderData.Items` type changed; unused `using` removed
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` — `Items` assignment projected; weight-calc line unchanged
- `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs` — reflection test added
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs` — 2 reflection tests added (DTO shape + symmetric WeightGrams guarantee)
- `frontend/src/api/generated/api-client.ts` — auto-regenerated; `weightGrams` absent from both DTO types

## Tests

- `GetPackingOrderHandlerTests.cs`: added `PackingOrderItemDto_HasExactlyTheFourPublicFields_AndNoWeightGrams`
- `ScanPackingOrderHandlerTests.cs`: added `ScanPackingOrderItemDto_HasExactlyTheFourPublicFields_AndNoWeightGrams` and `InternalPackingOrderItem_StillExposesWeightGrams_ForShipmentMath`
- All 4,456 backend tests pass (3 skipped); existing weight-eligibility tests pass unchanged

## How to verify

```bash
cd backend && dotnet build && dotnet test
cd frontend && npm run build
grep -n "weightGrams" frontend/src/api/generated/api-client.ts || echo "OK: absent"
grep -rn "List<PackingOrderItem>" backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/
```
Expected: all tests green; `weightGrams` absent from client; boundary grep returns zero matches.

## Notes

- Pre-existing frontend lint errors in 144 test files (unrelated to this change) — those existed before the branch and are out of scope per spec.
- NSwag required explicit invocation (`dotnet nswag run nswag.frontend.json`) rather than auto post-build, but the result is equivalent.
- All 4 arch-review amendments applied: `sealed class` on both DTOs, explicit projection-vs-weight-calc ordering in ScanPackingOrderHandler, OQ-1 resolved (no frontend consumer of `weightGrams`), symmetric reflection test anchoring `PackingOrderItem.WeightGrams`.

## PR Summary

Split `PackingOrderItem` — previously both an internal adapter contract and a public API DTO — into three clearly scoped types. The internal type retains `WeightGrams` for shipment-weight calculations; the two new module-local DTOs (`PackingOrderItemDto`, `ScanPackingOrderItemDto`) expose only the four public fields to API clients.

This prevents internal fields from leaking into the public contract: `WeightGrams` was already being serialized to the frontend (visible in the generated client), even though no frontend consumer used it. Both handlers now project explicitly at the response boundary. Reflection-based tests lock the API surface so any future internal field would fail CI before reaching clients.

### Changes
- `IPackingOrderClient.cs` — tightened doc on `PackingOrderItem` to declare it an internal contract
- `PackingOrderItemDto.cs` — new sealed DTO (4 fields) for GetPackingOrder use case
- `GetPackingOrderResponse.cs` — `Items` now `List<PackingOrderItemDto>`
- `GetPackingOrderHandler.cs` — explicit projection from internal → DTO
- `ScanPackingOrderItemDto.cs` — new sealed DTO (4 fields) for ScanPackingOrder use case
- `ScanPackingOrderResponse.cs` — `ScanOrderData.Items` now `List<ScanPackingOrderItemDto>`; unused `using` removed
- `ScanPackingOrderHandler.cs` — explicit projection; weight-calc line reads from internal collection unchanged
- `GetPackingOrderHandlerTests.cs` / `ScanPackingOrderHandlerTests.cs` — reflection tests lock DTO shape
- `api-client.ts` — regenerated; `weightGrams` absent from both packing-order DTO types

## Status
DONE