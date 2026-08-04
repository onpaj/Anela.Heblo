# Review: DepartmentsController MediatR migration (commit b4bd9f27)

## Verdict: done

## What was checked

Read plan-01.md, design-01.md, architecture-01.md, development-01.md, then read the actual diff in `b4bd9f27` file-by-file (not just the summary), plus the surrounding code it touches (`BaseApiController.HandleResponse`, `ErrorCodes.cs`, `module-map.md` §21/§35/§44, and every frontend consumer of `Department`/`useDepartments`).

## Conformance to the original arch-review finding

- `DepartmentsController` no longer injects `IDepartmentClient` or returns `ActionResult<IEnumerable<Department>>` (raw domain entity). It now extends `BaseApiController`, injects `IMediator`, sends `GetDepartmentsRequest`, and returns `HandleResponse(response)` — closing the ADR-003 MediatR-bypass finding exactly as scoped.
- Response is now `GetDepartmentsResponse : BaseResponse` wrapping `List<DepartmentDto>` (`Contracts/DepartmentDto.cs`, plain class per the "DTOs are classes" rule) — no domain type on the wire.
- `HandleResponse`'s `ErrorCodes.InternalServerError` → HTTP 500 mapping verified directly in `BaseApiController.cs` and `ErrorCodes.cs:32-33`; `GetDepartmentsHandler`'s catch-all uses this correctly, matching `GetGroupMembersHandler`'s pattern.

## Conformance to architecture-01.md's required addition (module-boundary fix)

This was the one gap the architecture step flagged as required, not optional. It's fully implemented:
- New `IDepartmentQueryService` (module-35-owned, `Application/Features/UserManagement/Services/`, mirrors `IGraphService`'s placement) returns `DepartmentDto` directly — confirmed no `using Anela.Heblo.Domain.Features.InvoiceClassification` anywhere under `Application/Features/UserManagement/` (grep is empty).
- `FlexiDepartmentQueryService` (the only file in the change set referencing the `InvoiceClassification` domain type) implements it by wrapping the existing `IDepartmentClient` and mapping `Department → DepartmentDto`. It lives in `Adapters.Flexi`, which `module-map.md` §44 assigns to a separate "FlexiBee ERP Adapter" module — the same shape as the `IGraphService`/`GraphService` precedent cited in architecture-01.md (contract owned by the consuming module, implemented by an adapter module). DI registered alongside the existing `IDepartmentClient` registration in `FlexiAdapterServiceCollectionExtensions.cs`, without touching the still-needed `DepartmentSyncService` registration.

## Frontend

- `useDepartments.ts` now unwraps the new envelope (`body.departments`). Its exported `Department` interface is hook-local (not imported from the generated client), so the two downstream consumers (`RuleForm.tsx`, `RulesList.tsx`) are unaffected — verified by reading both files.
- `api-client.ts` hand-patch: `departments_GetDepartments()` now returns `GetDepartmentsResponse`, `processDepartments_GetDepartments` deserializes via `GetDepartmentsResponse.fromJS` and adds a 500 branch; `Department`/`IDepartment` are replaced by `DepartmentDto`/`IDepartmentDto`. Shape matches what NSwag would generate for a `BaseResponse`-derived type elsewhere in the same file.

## Tests

All three required test files exist and are meaningful (not placeholder assertions): `GetDepartmentsHandlerTests` (success/empty/exception→500), `DepartmentsControllerTests` (200 pass-through, failure→500 via `HandleResponse`, controller does no side work — `VerifyNoOtherCalls`), `FlexiDepartmentQueryServiceTests` (mapping, empty list).

## Independent verification performed this step (not just trusting development-01.md's claims)

- `dotnet build Anela.Heblo.sln` — 0 errors, 250 warnings (pre-existing, none new).
- `dotnet test --filter "GetDepartmentsHandlerTests|DepartmentsControllerTests"` — 6/6 passed.
- `dotnet test --filter "FlexiDepartmentQueryServiceTests"` — 2/2 passed.
- `npm run build` (frontend) — compiled successfully.

## Non-blocking observations (not requiring another round)

- `FlexiAdapterServiceCollectionExtensions.cs`'s new `using Anela.Heblo.Application.Features.UserManagement.Services;` line lands in an already non-alphabetized using block (pre-existing disorder, e.g. `Domain.Features.Catalog.Products` sits mid-`Adapters.Flexi.*` block) — consistent with existing file state, `dotnet format --verify-no-changes` passed per development-01.md.
- Frontend `npm run lint` was not independently re-run this step (relied on development-01.md's report of pre-existing, unrelated failures); the build compiling cleanly and the two consumer files being untouched in shape gives enough confidence this wasn't rechecked from scratch.

No functional requirement, architecture instruction, or required test is missing. No correctness bug found.
