# Plan: DepartmentsController — route through MediatR with a proper DTO

## Summary
`DepartmentsController.GetDepartments` (`backend/src/Anela.Heblo.API/Controllers/DepartmentsController.cs:8-22`) injects the domain client `IDepartmentClient` directly and serializes the domain entity `Anela.Heblo.Domain.Features.InvoiceClassification.Department` onto the wire. It is the only business controller in the solution that bypasses MediatR. This plan adds a standard MediatR use case (`GetDepartments`) with a `DepartmentDto` contract in the owning module (#35, Users/Identity/OrgChart), and updates the controller to delegate through `HandleResponse`, matching `UserManagementController`.

## Context
Confirmed by reading the code directly:
- `DepartmentsController` (7 lines of logic) has no `Request`/`Response`, no validation, no `BaseResponse`/`ErrorCode` — an exception in `IDepartmentClient.GetDepartmentsAsync` surfaces as an unmapped 500, unlike every sibling endpoint in module 35 (`UserManagementController`, `OrgChartController`).
- Domain entity is trivial: `Department { string Id, string Name }` (`Domain/Features/InvoiceClassification/Department.cs`). `IDepartmentClient` (same namespace) has `GetDepartmentsAsync` and `GetDepartmentByIdAsync`; only `GetDepartmentsAsync` is used by the controller.
- Module map (`docs/architecture/module-map.md:801-820`) explicitly assigns `DepartmentsController.cs` to module 35 "Users, Identity & Org Chart", alongside `UserManagementController`/`OrgChartController` and their `Application/Features/UserManagement/` code — even though the underlying `IDepartmentClient`/`Department` types live in the `InvoiceClassification` domain namespace. That domain-ownership mismatch is called out as explicitly out of scope by the arch-review; this plan only fixes the controller-side MediatR/contract bypass, reusing `IDepartmentClient` from its existing namespace via a cross-module domain dependency (no different from what the controller does today).
- Established sibling pattern to copy is `UserManagementController.GetGroupMembers` → `GetGroupMembersRequest`/`GetGroupMembersResponse` (`BaseResponse` subtype) → `GetGroupMembersHandler`, with `UserDto` in `Application/Features/UserManagement/Contracts/`. `BaseApiController.HandleResponse<T>` maps `Success`/`ErrorCode` to the right HTTP status.
- **Frontend impact discovered during investigation (not in the original report):** `frontend/src/api/hooks/useDepartments.ts` does a manual `fetch` against `/api/departments` (bypassing the generated OpenAPI client) and expects the response body to be a raw `Department[]` array (`{ id, name }[]`). The generated client (`frontend/src/api/generated/api-client.ts:2983`) also currently types `departments_GetDepartments(): Promise<Department[]>` against the same raw-array shape. Wrapping the response in a `BaseResponse`-derived envelope (as the target pattern requires) changes the JSON shape from a bare array to `{ success, errorCode, departments: [...] }`, which will break both consumers unless updated in the same change.

## Functional requirements

**FR-1 — Add `DepartmentDto` contract.**
New class `Anela.Heblo.Application/Features/UserManagement/Contracts/DepartmentDto.cs`: `{ string Id, string Name }`, plain C# class (not a record, per project DTO rule).
Acceptance: DTO exists, is a class, mirrors the two fields of the domain `Department` entity, no domain-namespace `using` in the DTO file.

**FR-2 — Add `GetDepartments` MediatR use case.**
New folder `Anela.Heblo.Application/Features/UserManagement/UseCases/GetDepartments/`:
- `GetDepartmentsRequest : IRequest<GetDepartmentsResponse>` — no parameters.
- `GetDepartmentsResponse : BaseResponse` — `List<DepartmentDto> Departments { get; set; } = new();`
- `GetDepartmentsHandler : IRequestHandler<GetDepartmentsRequest, GetDepartmentsResponse>` — depends on `IDepartmentClient` (constructor-injected from `Domain.Features.InvoiceClassification`), calls `GetDepartmentsAsync(cancellationToken)`, maps each `Department` to a `DepartmentDto`, returns success. On exception, catch and return `ErrorCode = ErrorCodes.InternalServerError` (matching `GetGroupMembersHandler`'s catch-all shape) rather than letting it throw past the controller.
Acceptance: handler unit-testable by mocking `IDepartmentClient`; a thrown exception from the client produces a `Success = false` / `ErrorCode = InternalServerError` response, not an unhandled exception.

**FR-3 — Rewrite `DepartmentsController`.**
Replace `IDepartmentClient` dependency with `IMediator`; extend `BaseApiController` instead of `ControllerBase`; `GetDepartments` action sends `GetDepartmentsRequest` and returns `HandleResponse(response)`. Remove the now-unused `using Anela.Heblo.Domain.Features.InvoiceClassification;`.
Acceptance: controller has no reference to `Domain.Features.InvoiceClassification` or `IDepartmentClient`; return type is `ActionResult<GetDepartmentsResponse>`.

**FR-4 — Regenerate OpenAPI/TypeScript client and update `useDepartments.ts`.**
Run the project's client-generation step (per `docs/development/api-client-generation.md`) so `api-client.ts` reflects the new `GetDepartmentsResponse` shape. Update `frontend/src/api/hooks/useDepartments.ts`'s manual fetch to parse `{ departments: [...] }` and unwrap to the array the hook's callers expect (keep the hook's existing return type — `Department[]` — as its external contract so `FinancialFilters.tsx`, `RuleForm.tsx`, `RulesList.tsx` need no changes).
Acceptance: `npm run build` succeeds; existing consumers of `useDepartments()` compile unchanged; manual/E2E check of the Departments-consuming UI (e.g. Financial Overview filters) still lists departments.

**FR-5 — No handler-level validation needed.** `GetDepartmentsRequest` has no input parameters, so (unlike `GetGroupMembersRequestValidator`) no FluentValidation validator is required.

## Non-functional requirements
- No behavior change for callers of `useDepartments()` beyond the wire format handled internally by FR-4 — response data (department id/name list) is unchanged.
- No new authorization requirement introduced; match existing (implicit, no `[FeatureAuthorize]`) access on `DepartmentsController` — out of scope to add one now.
- Failure path must map to the app's standard typed error contract (`BaseResponse`/`ErrorCodes`) instead of an unmapped 500.

## Data model
- `DepartmentDto { Id: string, Name: string }` — new contract type, structurally identical to domain `Department`.
- `GetDepartmentsRequest` — empty request marker.
- `GetDepartmentsResponse : BaseResponse { Departments: List<DepartmentDto> }`.
- No persistence/schema changes; `IDepartmentClient`/domain `Department` are untouched.

## Interfaces
- `GET /api/Departments` — unchanged route and verb. Response body changes from a raw JSON array to `{ success, errorCode, params, departments: [...] }` (the standard envelope every other module-35 endpoint already uses).

## Dependencies and scope
- Depends on: existing `IDepartmentClient` registration (`FlexiAdapterServiceCollectionExtensions.cs:86`) — unchanged.
- In scope: controller, new use case, new DTO, OpenAPI client regen, `useDepartments.ts` update.
- Out of scope (explicitly, per the report): moving `IDepartmentClient`/`Department` out of the `InvoiceClassification` domain namespace into a module-35-owned domain namespace — that is a separate ownership question. Also out of scope: adding `[FeatureAuthorize]` to the controller, and touching `GetDepartmentByIdAsync` (unused by any controller today).

## Rough plan
1. Add `DepartmentDto` under `Application/Features/UserManagement/Contracts/`.
2. Add `GetDepartmentsRequest`/`GetDepartmentsResponse`/`GetDepartmentsHandler` under `Application/Features/UserManagement/UseCases/GetDepartments/`, mapping domain `Department` → `DepartmentDto`.
3. Rewrite `DepartmentsController` to extend `BaseApiController`, inject `IMediator`, delegate via `HandleResponse`.
4. `dotnet build` + `dotnet format`; add/adjust a handler unit test analogous to existing `GetGroupMembers` handler tests if such a test project convention exists for UserManagement use cases.
5. Regenerate the TypeScript OpenAPI client; update `useDepartments.ts` to unwrap `response.departments`.
6. `npm run build` + `npm run lint`; manually sanity-check a Departments-consuming screen (Financial Overview filters) against the running app.

## Open questions
- Should `GetDepartmentByIdAsync` also get a use case now, or left until it's actually consumed by a controller? Default: leave it — no controller calls it today, adding an unused use case would violate "don't build for hypothetical future requirements."
- Should the new use case live under `UserManagement/UseCases/` (chosen, since module 35 owns the controller and this mirrors `GetGroupMembers`) or a new `Departments/` sub-folder within the same module for clearer naming? Default: reuse `UserManagement/UseCases/GetDepartments/` — the module already mixes org-chart/user-management/department concerns under one Application folder, so a new top-level module folder isn't warranted for one use case.
