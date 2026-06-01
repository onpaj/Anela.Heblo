Now writing the output artifact.

---

# Implementation: Move `BlockOrderRequest` DTO from API to Application layer

## What was implemented

Relocated the `BlockOrderRequest` class from the bottom of `ShoptetOrdersController.cs` (lines 56–60) into a dedicated file in the Application layer's `BlockOrderProcessing` use-case folder. The controller now references the type from the Application namespace (the `using` was already present), and the unused `using System.Text.Json.Serialization;` was removed from the controller.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderRequest.cs` — new file containing the relocated class with file-scoped namespace, `class` keyword (not `record`), and `[JsonPropertyName("note")]` preserved verbatim
- `backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs` — removed inline `BlockOrderRequest` class definition and the now-unused `using System.Text.Json.Serialization;` directive

## Tests

No new tests required per NFR-2 (pure relocation). Existing tests (`BlockOrderProcessingHandlerTests`, `BlockOrderProcessingIntegrationTests`) reference only the MediatR request, not the HTTP DTO — verified by grep, no test updates needed.

## How to verify

```bash
# Build succeeds with 0 errors
dotnet build Anela.Heblo.sln

# DTO no longer defined in API project
grep -r "class BlockOrderRequest" backend/src/Anela.Heblo.API/   # → no output

# DTO exists in Application layer
grep -n "class BlockOrderRequest" backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderRequest.cs
# → 3: public class BlockOrderRequest

# No fully-qualified API references remain
grep -r "Anela.Heblo.API.Controllers.BlockOrderRequest" backend/   # → no output
```

## Notes

- The `[JsonPropertyName("note")]` attribute requires `using System.Text.Json.Serialization;` in the new file — this is fine since `System.Text.Json` is part of the .NET 8 BCL (no package change).
- The two-type design (`BlockOrderRequest` HTTP DTO + `BlockOrderProcessingRequest` MediatR request) is intentionally preserved per spec "Out of Scope."
- `ShipmentLabelsController.cs` exhibits the same anti-pattern (DTO defined inline); flagged in the arch review as a follow-up, not addressed here per scope discipline.

## PR Summary

Relocated the `BlockOrderRequest` HTTP body DTO from the bottom of `ShoptetOrdersController.cs` into `Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderRequest.cs`, restoring the architectural rule that the API project must not own DTOs — it only uses them.

The API controller already imported the `BlockOrderProcessing` namespace, so no new `using` was needed. The only controller-side change is deleting the inline class definition and removing the `using System.Text.Json.Serialization;` that existed solely for it. Zero behavior change; `dotnet build` succeeds with no new warnings.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderRequest.cs` — new file: relocated DTO with file-scoped namespace, class keyword, `[JsonPropertyName("note")]` preserved
- `backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs` — removed inline `BlockOrderRequest` class (lines 56–60) and the now-unused `using System.Text.Json.Serialization;`

## Status
DONE