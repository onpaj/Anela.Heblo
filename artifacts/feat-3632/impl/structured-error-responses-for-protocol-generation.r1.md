# Implementation: structured-error-responses-for-protocol-generation

## What was implemented
Replaced the two `throw new InvalidOperationException(...)` business-rule failures in `GetManufactureProtocolHandler.Handle` (order not found, order not completed) with early-return structured `GetManufactureProtocolResponse` error results, added a new `ErrorCodes.ManufactureOrderNotCompleted = 1217` (`[HttpStatusCode(HttpStatusCode.BadRequest)]`), and updated `ManufactureOrderController.GetProtocolPdf` to drop its try/catch and delegate to the inherited `HandleResponse` helper — matching the pattern used by every other handler/action pair in the Manufacture module (e.g. `ResolveManualAction`, `GetSemiproductRecipePdfHandler`). The four existing tests that asserted throw-based behavior were rewritten to assert the structured-response contract instead.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs` — replaced the two throw sites with early `return new GetManufactureProtocolResponse(ErrorCodes.OrderNotFound, ...)` / `return new GetManufactureProtocolResponse(ErrorCodes.ManufactureOrderNotCompleted, ...)`; added `using Anela.Heblo.Application.Shared;`.
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — added `ManufactureOrderNotCompleted = 1217` to the Manufacture module (12XX) block, tagged `[HttpStatusCode(HttpStatusCode.BadRequest)]`, immediately after `ManufacturedInventoryInsufficientStock = 1216`.
- `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs` — changed `GetProtocolPdf` signature from `Task<IActionResult>` to `Task<ActionResult<GetManufactureProtocolResponse>>`; removed the try/catch; now checks `response.Success` and calls `HandleResponse(response)` on failure, `File(...)` on success.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs` — renamed and rewrote `Handle_OrderNotFound_Throws` → `Handle_OrderNotFound_ReturnsErrorResponse` and `Handle_NonCompletedOrder_Throws` → `Handle_NonCompletedOrder_ReturnsErrorResponse`; both now assert `result.Success == false` and the correct `ErrorCode` instead of expecting an exception. Added `using Anela.Heblo.Application.Shared;`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderControllerProtocolTests.cs` — renamed `GetProtocolPdf_Should_Return_BadRequest_When_Order_Not_Found` → `GetProtocolPdf_Should_Return_NotFound_When_Order_Not_Found`; both error tests now mock the mediator returning a failed `GetManufactureProtocolResponse` (instead of `ThrowsAsync`) and assert on `result.Result` type (`NotFoundObjectResult` / `BadRequestObjectResult`). The success-path test's unwrap accessor was updated from `result.Should()...` to `result.Result.Should()...` to match the new `ActionResult<T>` return type (its other assertions are unchanged). Added `using Anela.Heblo.Application.Shared;`.

## Tests
- `GetManufactureProtocolHandlerTests.cs` — 10 tests total (2 rewritten, 8 pre-existing success-path/mapping tests untouched), all passing.
- `ManufactureOrderControllerProtocolTests.cs` — covers the success path (`FileResult`), the not-found path (404 `NotFoundObjectResult`), and the not-completed path (400 `BadRequestObjectResult`), all passing.

## How to verify
```bash
cd /home/user/worktrees/feature-3632-Arch-Review-Manufacture-Getmanufactureprotocolhand
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~GetManufactureProtocolHandlerTests|FullyQualifiedName~ManufactureOrderControllerProtocolTests" --no-build
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture" --no-build
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderControllerProtocolTests.cs
```
Results: build succeeded (0 errors, 1 pre-existing unrelated warning from an access-matrix codegen tool that is not touched by this change); the two targeted test files pass 10/10; the full Manufacture-scoped test suite passes 755/755 (no regressions); `dotnet format --verify-no-changes` reports no diffs on the touched files.

## Notes
- No existing caller references `GetProtocolPdf`'s old `IActionResult` return type or the removed exception-throwing behavior (confirmed via grep across `backend/`).
- `dotnet build`/`dotnet test` initially appeared to hang for several minutes due to a cluster of stray concurrent MSBuild/dotnet processes left over from earlier pipeline runs contending for build-server resources in this sandbox; killing them and re-running cleanly resolved it in seconds. Not a code issue.
- The pre-existing `MSB3073`/access-matrix-generation warning during `dotnet build` (a `System.Text.Json.JsonException` inside `Anela.Heblo.AccessMatrixGen`) is unrelated to this change — it occurs on the unmodified `Anela.Heblo.API.csproj` post-build step and does not fail the build.
- The response shape for the two error cases is a genuine, intentional breaking change (plain `{ message }` string → structured `GetManufactureProtocolResponse` JSON with `ErrorCode`/`Params`; not-found status changes 400 → 404), as specified and endorsed by the spec/architecture-review artifacts. No frontend consumer of this specific error path was found in scope.

## PR Summary
`GetManufactureProtocolHandler` was the only handler in the Manufacture module that reported business-rule failures ("order not found", "order not in Completed state") by throwing `InvalidOperationException`, forcing `ManufactureOrderController.GetProtocolPdf` to catch and translate exceptions into HTTP responses — a business-error-translation responsibility that belongs in the handler per this project's "business logic in handlers, not controllers" rule. It was also fragile: any unrelated `InvalidOperationException` from deeper in the call stack would be silently caught and returned as a generic 400, masking real errors.

This change brings the handler in line with every other handler in the module. The two business-rule failures now return a structured `GetManufactureProtocolResponse` with `Success = false` and an `ErrorCode` — `ErrorCodes.OrderNotFound` (existing code, reused) for the not-found case, and a new `ErrorCodes.ManufactureOrderNotCompleted = 1217` for the not-completed case. The controller no longer has a try/catch; it delegates to the shared `HandleResponse` helper like every other action in `ManufactureOrderController`, which maps `ErrorCodes` to the correct HTTP status via the `HttpStatusCodeAttribute` already on the enum. As a result, "order not found" now correctly returns 404 (previously an undifferentiated 400), and "not completed" continues to return 400, both now with a structured JSON error body instead of a plain message string.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs` — throws replaced with structured early returns
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — added `ManufactureOrderNotCompleted = 1217`
- `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs` — `GetProtocolPdf` now uses `HandleResponse`, no try/catch
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs` — rewrote the two throw-asserting tests
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderControllerProtocolTests.cs` — rewrote the two throw-asserting tests, updated the success test's result accessor

## Status
DONE
