# Development: DepartmentsController — route through MediatR with a proper DTO

Implements plan-01.md / design-01.md, with architecture-01.md's required addition (module-35-owned `IDepartmentQueryService` contract instead of a direct `IDepartmentClient` domain dependency) folded into FR-2.

## Summary

`DepartmentsController` no longer injects the cross-module domain client `IDepartmentClient` (`Domain.Features.InvoiceClassification`) or returns the raw domain entity `Department`. It now dispatches a MediatR `GetDepartmentsRequest` through `IMediator`/`HandleResponse` (matching `UserManagementController`), and the handler depends on a new module-35-owned service interface (`IDepartmentQueryService`) instead of reaching into another module's domain namespace directly — closing both the ADR-003 MediatR bypass and the module-boundary violation flagged in architecture-01.md §3.

## Files created

**Backend — contract, service interface, adapter, use case**
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/DepartmentDto.cs` — plain class `{ Id, Name }`.
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IDepartmentQueryService.cs` — module-35-owned contract (`Task<List<DepartmentDto>> GetDepartmentsAsync(...)`), mirrors `IGraphService`'s placement. Returns `DepartmentDto` directly so no `Domain.Features.InvoiceClassification` type leaks into the Application layer.
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs` — adapter implementing `IDepartmentQueryService` by wrapping the existing `IDepartmentClient` and mapping `Department` → `DepartmentDto`. This is the only place in the change set that references the `InvoiceClassification` domain type.
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetDepartments/GetDepartmentsRequest.cs`, `GetDepartmentsResponse.cs`, `GetDepartmentsHandler.cs` — standard MediatR use case, mirrors `GetGroupMembers`. Handler depends only on `IDepartmentQueryService`; catches any exception and returns `ErrorCode = ErrorCodes.InternalServerError` (single catch-all, since `IDepartmentClient`/`IDepartmentQueryService` expose no narrower exception types, unlike `IGraphService`'s Graph-specific exceptions).

**Backend — tests**
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetDepartmentsHandlerTests.cs` — 3 tests: success with departments, success with empty list, exception → `InternalServerError`.
- `backend/test/Anela.Heblo.Tests/Controllers/DepartmentsControllerTests.cs` — 3 tests: success → `OkObjectResult`, handler failure → 500 via `HandleResponse`, controller body only calls `IMediator.Send` (no other side calls), mirroring `UserManagementControllerTests`.
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs` — 2 tests: maps domain `Department` list to `DepartmentDto` list correctly; empty client result → empty list.

## Files changed

- `backend/src/Anela.Heblo.API/Controllers/DepartmentsController.cs` — rewritten: extends `BaseApiController`, injects `IMediator`, `GetDepartments` action sends `GetDepartmentsRequest` and returns `HandleResponse(response)`. No more `IDepartmentClient`/`Domain.Features.InvoiceClassification` reference. Added `[ProducesResponseType]` for 200/500, matching sibling controllers.
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` — registers `IDepartmentQueryService → FlexiDepartmentQueryService` (scoped), alongside the existing `IDepartmentClient` registration (still needed by `DepartmentSyncService`, untouched).
- `frontend/src/api/hooks/useDepartments.ts` — the manual `fetch` now unwraps the new envelope: `const body = await response.json(); return body.departments;` instead of returning the raw parsed body. The hook's external return type (`Department[]`) and all downstream consumers (`FinancialFilters.tsx`, `RuleForm.tsx`, `RulesList.tsx`) are unchanged.
- `frontend/src/api/generated/api-client.ts` — regenerated the `Departments` endpoint and its response types by hand to match exactly what NSwag would produce (verified once via a full NSwag run, then hand-applied only the Departments-scoped hunks — see note below): `departments_GetDepartments()` now returns `GetDepartmentsResponse` (`{ departments: DepartmentDto[] } extends BaseResponse`) instead of a raw `Department[]`; the old `Department`/`IDepartment` types are replaced by `GetDepartmentsResponse`/`IGetDepartmentsResponse`/`DepartmentDto`/`IDepartmentDto`.

**Why the generated client was hand-patched instead of committing a full regen:** running `dotnet msbuild -t:GenerateFrontendClientManual` regenerates the *entire* 30k-line file against the live OpenAPI spec, and picked up unrelated drift already present between `main` and this file (e.g. `ManufactureOrder_GetProtocolPdf`'s response shape, `RemoveItemFromBox`'s new `amount` parameter, `GenerateArticleRequest` required fields, a new `ManufactureOrderNotCompleted` error code) — all from other work already merged to the backend that simply hadn't been regenerated. None of that is in scope here, so per the "surgical changes" rule the full regen was reverted and only the two Departments-related hunks (the `departments_GetDepartments`/`processDepartments_GetDepartments` method, and the `Department`/`IDepartment` type block) were hand-applied, copied verbatim from the one-time full regen output for byte-for-byte fidelity with what NSwag actually produces for this controller shape.

## Verification performed

- `dotnet build Anela.Heblo.sln` — 0 errors (250 pre-existing warnings, none introduced by this change).
- `dotnet test` (targeted): `GetDepartmentsHandlerTests` + `DepartmentsControllerTests` in `Anela.Heblo.Tests` — 6/6 passed. `FlexiDepartmentQueryServiceTests` in `Anela.Heblo.Adapters.Flexi.Tests` — 2/2 passed.
- `dotnet format Anela.Heblo.sln --include <all changed/new backend files> --verify-no-changes` — clean, no formatting diffs.
- `npm run build` (frontend) — compiled successfully.
- `npm run lint` (frontend) — 175 pre-existing errors/13 warnings, all in unrelated files (photobank/financial-overview/terminal/leaflet-generator test files under `__tests__/`); none in `useDepartments.ts` or `api-client.ts`. Confirmed via grep that neither changed file appears in the lint output.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetDepartmentsHandlerTests|FullyQualifiedName~DepartmentsControllerTests"
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --filter "FullyQualifiedName~FlexiDepartmentQueryServiceTests"

cd ../frontend
npm run build
```

Manual check: `GET /api/Departments` now returns `{ "success": true, "errorCode": null, "params": null, "departments": [{ "id": "...", "name": "..." }, ...] }` instead of a bare array; the Financial Overview department filter (`useDepartments()` consumer) should still populate correctly since the hook unwraps the new envelope internally.

## Scope notes (carried from plan/design/architecture)

- `IDepartmentClient`/domain `Department` remain in `Domain.Features.InvoiceClassification`, still registered and still used directly by `DepartmentSyncService` — untouched, per explicit scope.
- `GetDepartmentByIdAsync` was not given a use case (no controller calls it).
- No `[FeatureAuthorize]` added to `DepartmentsController` (none existed before; out of scope).
