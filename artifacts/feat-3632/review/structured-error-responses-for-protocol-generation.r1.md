# Code Review: structured-error-responses-for-protocol-generation

## Summary
The implementation replaces both `throw new InvalidOperationException` sites in `GetManufactureProtocolHandler.Handle` with structured `GetManufactureProtocolResponse` early returns, adds `ErrorCodes.ManufactureOrderNotCompleted = 1217`, and updates `ManufactureOrderController.GetProtocolPdf` to delegate to `HandleResponse` instead of try/catch — exactly matching the task-context's prescribed code, file list, and test rewrites. Verified directly against commit `d0d1c07` and confirmed the build, targeted tests, and `dotnet format` all pass as claimed.

## Review Result: PASS

### task: structured-error-responses-for-protocol-generation
**Status:** PASS

## Overall Notes
Verification performed directly, not taken on faith from the implementation report:
- `git show d0d1c07` diff matches the spec's prescribed code almost verbatim: handler early-returns on `order == null` (`ErrorCodes.OrderNotFound`, `orderId` param) and on non-`Completed` state (`ErrorCodes.ManufactureOrderNotCompleted`, `orderId`/`state` params); `ErrorCodes.ManufactureOrderNotCompleted = 1217` was inserted in the correct place in the 12XX block, tagged `[HttpStatusCode(HttpStatusCode.BadRequest)]`, with no renumbering of `OrderNotFound = 1210` or any other existing value; controller signature changed to `Task<ActionResult<GetManufactureProtocolResponse>>`, try/catch removed, `HandleResponse(response)` used on failure.
- Both test files were rewritten per the spec: `Handle_OrderNotFound_ReturnsErrorResponse` / `Handle_NonCompletedOrder_ReturnsErrorResponse` assert `Success == false` + correct `ErrorCode` with no exception path; controller tests mock `ReturnsAsync` instead of `ThrowsAsync` and assert `result.Result` type (`NotFoundObjectResult` for 404, `BadRequestObjectResult` for 400); the success-path test's unwrap accessor was updated to `result.Result.Should()...` per the documented gotcha, with no other assertion changed.
- Ran `dotnet build Anela.Heblo.sln`: 0 errors, 249 pre-existing warnings, none introduced by or located in the touched files.
- Ran `dotnet test --filter "FullyQualifiedName~GetManufactureProtocolHandlerTests|FullyQualifiedName~ManufactureOrderControllerProtocolTests" --no-build`: 10/10 passed, matching the implementation report's claim.
- Ran `dotnet format --verify-no-changes --include <the 5 touched files>`: no output/diffs, confirming formatting compliance.
- Grepped `backend/` for `GetProtocolPdf`: only the controller and its test file reference it — no other caller depends on the old `IActionResult`/throw-based contract, confirming acceptance criterion "no other file references the old return type."
- Frontend (`useManufactureOrders.ts`, generated `api-client.ts`, e2e `protocol.spec.ts`) references `protocol.pdf` but was correctly out of scope per the task-context's "Files to create/modify" list, which is backend-only; the implementer's note that no frontend consumer was found in scope is consistent with this review's grep.
