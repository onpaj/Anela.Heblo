# Code Review r1 — feat-4133

## Result: CLEAN

## Scope
Diff of `feature/4133-Arch-Review-Purchase-Three-Request-Dtos-Declared-A` against `main`'s merge-base, covering both planned tasks:
- `convert-get-purchase-order-requests`
- `convert-update-purchase-order-status-request`

## Verification against spec.r1.md acceptance criteria

- `GetPurchaseOrderByIdRequest` converted from `record GetPurchaseOrderByIdRequest(int Id)` to a class with `public int Id { get; set; }`. ✅
- `GetPurchaseOrderHistoryRequest` converted from `record GetPurchaseOrderHistoryRequest(int Id)` to a class with `public int Id { get; set; }`. ✅
- `UpdatePurchaseOrderStatusRequest` converted from a positional record with `(int Id, string Status)` to a class with `public int Id { get; set; }` and `public string Status { get; set; } = null!;`. ✅
- All three still implement their original `IRequest<TResponse>` contracts unchanged. ✅
- `PurchaseOrdersController` call sites updated to object-initializer syntax (`new GetPurchaseOrderByIdRequest { Id = id }`, `new GetPurchaseOrderHistoryRequest { Id = id }`). ✅
- Test call sites updated to object-initializer syntax across `PurchaseOrdersControllerTests.cs`, `GetPurchaseOrderHistoryHandlerTests.cs`, and `UpdatePurchaseOrderStatusHandlerTests.cs` (17 call sites). ✅
- No other module's request DTOs were touched; the fix stays scoped to the three named types per the issue. ✅

## Build & test verification (this session)

- `dotnet build Anela.Heblo.sln` — 0 errors (only pre-existing nullable-reference warnings unrelated to this change).
- `dotnet test .../Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Purchase"` — 344/345 passed. The one failure (`PurchaseOrderRepositoryHistorySqlShapeTests.GetHistoryAsync_EmitsSqlThatTouchesOnlyHistoryTable`) is a pre-existing environmental failure ("Docker is either not running or misconfigured" — a Testcontainers/Postgres fixture with no Docker daemon available in this sandbox), unrelated to the DTO conversion and not touching any of the changed files.
- `dotnet format Anela.Heblo.sln whitespace --verify-no-changes` on all seven touched files — clean, no changes needed.

## Findings

None blocking. No advisory findings either — the conversion is mechanical and matches the existing pattern used by the module's other request types (e.g. `CreatePurchaseOrderRequest`).
