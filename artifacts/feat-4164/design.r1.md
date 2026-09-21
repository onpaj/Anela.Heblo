# Design: PackingMaterials — co-locate misplaced MediatR Request/Response types

## Component Design

No new components, services, or interfaces. This is a physical relocation of eight existing classes (four Request/Response pairs) with no change to their public members or behavior:

- **`UseCases/GetPackingMaterialsList/`** — gains `GetPackingMaterialsListRequest.cs` (class `GetPackingMaterialsListRequest : IRequest<GetPackingMaterialsListResponse>`, no members) and `GetPackingMaterialsListResponse.cs` (class `GetPackingMaterialsListResponse : BaseResponse`, member `List<PackingMaterialDto> Materials`), split out of the current combined `Contracts/GetPackingMaterialsListRequest.cs`. Sits alongside the existing `GetPackingMaterialsListHandler.cs`.
- **`UseCases/CreatePackingMaterial/`** — gains `CreatePackingMaterialRequest.cs` (class `CreatePackingMaterialRequest : IRequest<CreatePackingMaterialResponse>`, members `Name`, `ConsumptionRate`, `ConsumptionType`, `CurrentQuantity`) and `CreatePackingMaterialResponse.cs` (class `CreatePackingMaterialResponse : BaseResponse`, members `Id`, `Material`), split out of the current combined `Contracts/CreatePackingMaterialRequest.cs`. Sits alongside the existing `CreatePackingMaterialHandler.cs`.
- **`UseCases/UpdatePackingMaterial/`** — gains `UpdatePackingMaterialRequest.cs` (class `UpdatePackingMaterialRequest : IRequest<UpdatePackingMaterialResponse>`, members `Id`, `Name`, `ConsumptionRate`, `ConsumptionType`) and `UpdatePackingMaterialResponse.cs` (class `UpdatePackingMaterialResponse : BaseResponse`, members `Material`, `Error`), split out of the current combined `Contracts/UpdatePackingMaterialRequest.cs`. Sits alongside the existing `UpdatePackingMaterialHandler.cs`.
- **`UseCases/UpdatePackingMaterialQuantity/`** — gains `UpdatePackingMaterialQuantityRequest.cs` (class `UpdatePackingMaterialQuantityRequest : IRequest<UpdatePackingMaterialQuantityResponse>`, members `Id`, `NewQuantity`, `Date`) and `UpdatePackingMaterialQuantityResponse.cs` (class `UpdatePackingMaterialQuantityResponse : BaseResponse`, members `Material`, `Error`), moved as-is (already separate files) from `Contracts/`. Sits alongside the existing `UpdatePackingMaterialQuantityHandler.cs`.

Each moved type's namespace changes from `Anela.Heblo.Application.Features.PackingMaterials.Contracts` to `Anela.Heblo.Application.Features.PackingMaterials.UseCases.{UseCaseName}`, matching the pattern already used by every other use case in this module (e.g. `UseCases.GetAllocations`, `UseCases.DeletePackingMaterial`).

**Consumers requiring a `using`/import update** (namespace change only, no logic change; full list carried from the architecture review's grounded grep):
- The four handlers themselves (each already lives in the target folder and currently imports the old `Contracts` namespace to reach its own Request/Response — that import is deleted; no new import needed since the types become local to the handler's own namespace).
- `Anela.Heblo.API/Controllers/PackingMaterialsController.cs` — remove `using ...Contracts;` (confirmed unused for anything else in that file), add `using` for `UseCases.GetPackingMaterialsList`, `UseCases.CreatePackingMaterial`, `UseCases.UpdatePackingMaterial` (`UseCases.UpdatePackingMaterialQuantity` is already imported).
- Five test files under `test/Anela.Heblo.Tests/Features/PackingMaterials/` (`GetPackingMaterialsListHandlerTests.cs`, `PackingMaterialCrudHandlerTests.cs`, `PackingMaterialLogPersistenceTests.cs`, `PackingMaterialsControllerNotFoundTests.cs`, `PackingMaterialsListQueryCountTests.cs`) — update `using` directives to match; no test logic or assertions change.

`Contracts/` retains all genuinely shared types (`PackingMaterialDto`, `ConsumptionDetailDto`, `ConsumptionGroupBy`, `ConsumptionGroupDto`, `IInvoiceConsumptionSource`, `InvoiceConsumptionHeader`, `MaterialConsumptionHistoryItemDto`, `PackingMaterialAllocationDto`, `PackingMaterialLogDto`, `PackingMaterialsTextHelper`, `CreateAllocationRequestBody`, `UpdateAllocationRequestBody`, `UpdateQuantityRequest`) — none of these are touched.

## Data Schemas

No schema changes of any kind:
- **Wire format** — HTTP request/response JSON shapes for `GET`/`POST`/`PUT` on `PackingMaterialsController`'s list/create/update/update-quantity actions are byte-for-byte identical before and after, since property names/types on the Request/Response classes are unchanged — only their C# namespace moves.
- **Database** — no entities, EF Core configurations, or migrations are touched.
- **Generated OpenAPI/TypeScript client** — driven by controller action signatures and DTO shapes (routes, verbs, JSON property names/types), not by internal C# namespaces, so the generated client is expected to be unaffected. Confirmed by running client generation post-change and diffing (see architecture review's Risks table) rather than assumed.
