# Specification: Structured error responses for GetManufactureProtocol

## Summary
`GetManufactureProtocolHandler` currently throws `InvalidOperationException` for two business-rule failures (order not found, order not in `Completed` state), forcing `ManufactureOrderController.GetProtocolPdf` to catch and translate exceptions into HTTP responses — a business-error-translation responsibility that belongs in the handler, not the controller. This change replaces the throws with a structured `GetManufactureProtocolResponse` carrying an `ErrorCodes` value, and updates the controller to use the shared `HandleResponse` helper, matching the pattern already used by every other handler in the Manufacture module (e.g. `GetSemiproductRecipePdfHandler`, `UpdateManufactureOrderScheduleHandler`, `DuplicateManufactureOrderHandler`).

## Background
This is an architecture-review finding (filed 2026-07-13) against `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs`. The project rule "business logic must be in MediatR handlers, NOT in controllers" is violated: the controller currently decides HTTP status codes by inspecting an exception type via `catch (InvalidOperationException)`. This is also fragile — any unrelated `InvalidOperationException` thrown deeper in the call stack (e.g. from an ERP client call) would be silently swallowed and returned as a generic 400 Bad Request, masking real errors.

Investigation of the codebase confirms:
- `GetManufactureProtocolResponse` (`.../GetManufactureProtocol/GetManufactureProtocolResponse.cs`) **already** inherits `BaseResponse` and already has the `(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)` constructor — it was scaffolded correctly but the handler never uses the error path. No changes are needed to this class.
- `ErrorCodes.OrderNotFound` (value `1210`, mapped to HTTP 404 via `[HttpStatusCode(HttpStatusCode.NotFound)]`) already exists in the Manufacture module range (12xx) and is already used by `DuplicateManufactureOrderHandler` for the identical "order not found" condition. This is the correct code to reuse here, not the generic `ErrorCodes.ResourceNotFound` suggested in the brief — the module already has a specific, established code for this exact case.
- No existing error code fits "order exists but is not in `Completed` state for protocol generation." `CannotUpdateCompletedOrder` / `CannotUpdateCancelledOrder` (used by `UpdateManufactureOrderScheduleHandler`) describe the *opposite* direction (blocking mutation of an already-completed order) and would be misleading if reused here. A new, specific code is needed.
- `BaseApiController.HandleResponse<T>` maps `ErrorCodes` to HTTP status via reflection over `HttpStatusCodeAttribute` and returns `Ok`/`NotFound`/`BadRequest`/etc. accordingly — this is the standard mechanism used by every other action in `ManufactureOrderController` (confirmed in `GetOrder`, `CreateOrder`, `ResolveManualAction`, `UpdateOrderSchedule`, etc.), all of which call `_mediator.Send(...)` followed by `return HandleResponse(response);` with no try/catch.
- `GetSemiproductRecipePdfHandler` confirms the reference pattern for a PDF-returning handler: no throwing, early `return new XxxResponse(ErrorCodes.Y, new Dictionary<string,string> {...})` for business failures, and a catch-all `catch (Exception ex) { return new XxxResponse(ErrorCodes.Exception, ...); }` around infrastructure calls.
- Two existing test files directly assert today's throw-based behavior and must be rewritten as part of this change: `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs` (`Handle_NonCompletedOrder_Throws`, `Handle_OrderNotFound_Throws`) and `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderControllerProtocolTests.cs` (`GetProtocolPdf_Should_Return_BadRequest_When_Order_Not_Completed`, `GetProtocolPdf_Should_Return_BadRequest_When_Order_Not_Found`).

## Functional Requirements

### FR-1: Handler returns structured error responses instead of throwing
`GetManufactureProtocolHandler.Handle` must not throw `InvalidOperationException` for the two known business-rule conditions. It must return a `GetManufactureProtocolResponse` with `Success = false` and an appropriate `ErrorCodes` value instead.

**Acceptance criteria:**
- When `_repository.GetOrderByIdAsync` returns `null`, the handler returns `new GetManufactureProtocolResponse(ErrorCodes.OrderNotFound, new Dictionary<string, string> { { "orderId", request.Id.ToString() } })` and does not throw.
- When the found order's `State != ManufactureOrderState.Completed`, the handler returns `new GetManufactureProtocolResponse(ErrorCodes.ManufactureOrderNotCompleted, new Dictionary<string, string> { { "orderId", order.OrderNumber }, { "state", order.State.ToString() } })` and does not throw.
- The success path (order found and completed) is unchanged: the handler builds `ManufactureProtocolData`, renders the PDF, and returns a `GetManufactureProtocolResponse` with `PdfBytes` and `FileName` populated (implicit `Success = true` via the parameterless base constructor).
- No other behavior of the handler (ERP document lookups, data mapping, rendering) changes.

### FR-2: New error code for "order not completed"
Add `ManufactureOrderNotCompleted` to the `ErrorCodes` enum in `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, in the Manufacture module block (12xx), using the next free value (`1217`, after existing `ManufacturedInventoryInsufficientStock = 1216`), tagged `[HttpStatusCode(HttpStatusCode.BadRequest)]` to preserve the current client-visible status code (400) for this condition.

**Acceptance criteria:**
- The enum member is added with an explicit numeric value (`1217`) following the file's existing convention of explicit values per module block.
- The `HttpStatusCodeAttribute` on the new member resolves to `400 Bad Request` via `BaseApiController.GetStatusCodeForError`.
- No existing enum values are renumbered or removed.

### FR-3: Controller delegates to `HandleResponse`
`ManufactureOrderController.GetProtocolPdf` must not catch `InvalidOperationException` or inspect exception messages. It must send the request via MediatR, check `response.Success`, and delegate error handling to the inherited `HandleResponse` helper, matching every other action in the controller.

**Acceptance criteria:**
- The action's return type changes from `Task<IActionResult>` to `Task<ActionResult<GetManufactureProtocolResponse>>` (matching the convention used by all other actions in `ManufactureOrderController`, and required because `HandleResponse<T>` returns `ActionResult<T>`).
- On success (`response.Success == true`), the action returns `File(response.PdfBytes, "application/pdf", response.FileName)` (a `FileContentResult`, which implicitly converts to `ActionResult<GetManufactureProtocolResponse>` since `FileResult` derives from `ActionResult`).
- On failure (`response.Success == false`), the action returns `HandleResponse(response)` — no manual `BadRequest(...)` construction, no try/catch block.
- `order.Id` not found now surfaces as HTTP 404 with a `GetManufactureProtocolResponse` JSON body (`ErrorCode: OrderNotFound`), and "not completed" surfaces as HTTP 400 with `ErrorCode: ManufactureOrderNotCompleted` — both are a behavior change from today's undifferentiated `400 Bad Request { message: string }` shape, but are consistent with how every other endpoint in this controller (and module) already reports errors.

### FR-4: Update existing tests to match the new contract
Existing tests that assert throw-based behavior must be rewritten to assert the structured-response contract; no test may be left asserting the old exception-based behavior.

**Acceptance criteria:**
- `GetManufactureProtocolHandlerTests.Handle_OrderNotFound_Throws` is replaced with a test asserting `result.Success == false` and `result.ErrorCode == ErrorCodes.OrderNotFound` (no exception).
- `GetManufactureProtocolHandlerTests.Handle_NonCompletedOrder_Throws` is replaced with a test asserting `result.Success == false` and `result.ErrorCode == ErrorCodes.ManufactureOrderNotCompleted` (no exception).
- `ManufactureOrderControllerProtocolTests.GetProtocolPdf_Should_Return_BadRequest_When_Order_Not_Found` is updated to mock the mediator returning a failed `GetManufactureProtocolResponse(ErrorCodes.OrderNotFound, ...)` (not `ThrowsAsync`) and asserts the result is `NotFoundObjectResult` (404), consistent with `OrderNotFound`'s `HttpStatusCodeAttribute`.
- `ManufactureOrderControllerProtocolTests.GetProtocolPdf_Should_Return_BadRequest_When_Order_Not_Completed` is updated to mock the mediator returning a failed `GetManufactureProtocolResponse(ErrorCodes.ManufactureOrderNotCompleted, ...)` (not `ThrowsAsync`) and asserts the result is `BadRequestObjectResult` (400).
- `GetProtocolPdf_Should_Return_FileResult_With_Pdf_ContentType` continues to pass unchanged (success path is untouched).
- Full existing coverage of the mapping-heavy success-path tests in `GetManufactureProtocolHandlerTests` (ERP document building, conditions-reading mapping, etc.) is preserved unmodified.

## Non-Functional Requirements

### NFR-1: Performance
No performance-relevant change. Both conditions are early-return short-circuits before any I/O (ERP client calls, PDF rendering) — replacing a throw with a return is not measurably different in cost. No new NFR targets apply.

### NFR-2: Security
No change to authentication/authorization. The endpoint remains covered by `[FeatureAuthorize(Feature.Manufacture_ManufactureOrders)]` at the controller level, unaffected by this change. No new sensitive data is exposed; the `Params` dictionary on error responses contains only the order ID and state, which are already visible to any caller with access to this endpoint (no PII, no internal implementation details beyond what the client already requested).

## Data Model
No persistent data model changes. One in-memory/contract-level addition:
- `ErrorCodes.ManufactureOrderNotCompleted = 1217` — new enum member in `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, `[HttpStatusCode(HttpStatusCode.BadRequest)]`.

`GetManufactureProtocolResponse` (unchanged, already correct):
```csharp
public class GetManufactureProtocolResponse : BaseResponse
{
    public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
    // GetManufactureProtocolResponse() : base()  -- success
    // GetManufactureProtocolResponse(ErrorCodes errorCode, Dictionary<string,string>? parameters = null) : base(errorCode, parameters) -- failure
}
```

## API / Interface Design
`GET /api/ManufactureOrder/{id}/protocol.pdf`

| Condition | Before | After |
|---|---|---|
| Order found, `State == Completed` | 200, `application/pdf` binary | 200, `application/pdf` binary (unchanged) |
| Order not found | 400, `{ "message": "Manufacture order {id} not found." }` | 404, JSON `GetManufactureProtocolResponse` body with `Success: false`, `ErrorCode: "OrderNotFound"` (== 1210), `Params: { "orderId": "{id}" }` |
| Order found, `State != Completed` | 400, `{ "message": "Manufacture order {orderNumber} is not completed; cannot generate protocol." }` | 400, JSON `GetManufactureProtocolResponse` body with `Success: false`, `ErrorCode: "ManufactureOrderNotCompleted"` (== 1217), `Params: { "orderId": "{orderNumber}", "state": "{state}" }` |

This is a breaking response-shape change for the two error cases (`message` string → structured `ErrorCode`/`Params` object; not-found status changes 400 → 404). No known frontend consumer of this specific error path was found in scope for this backend-only fix (out of scope — see below); if a frontend caller does inspect the error body shape or status code for this endpoint, it will need a corresponding update, tracked separately.

## Dependencies
- None new. Reuses existing `Anela.Heblo.Application.Shared.BaseResponse`, `ErrorCodes`, `HttpStatusCodeAttribute`, and `BaseApiController.HandleResponse` infrastructure already present in the codebase.

## Out of Scope
- Any change to PDF rendering logic (`IManufactureProtocolRenderer`, `ManufactureProtocolData` mapping).
- Any change to `GetSemiproductRecipePdfHandler` or other already-compliant handlers (they are reference patterns only).
- Frontend changes to consume the new error response shape, if any frontend code currently parses this endpoint's error body — none was located during this review, but a frontend-side check/update is not part of this backend-only spec.
- Broader refactor of other handlers or controllers beyond `GetManufactureProtocolHandler` / `ManufactureOrderController.GetProtocolPdf`.
- Wrapping the handler's I/O (ERP client calls) in a catch-all `try/catch (Exception) → ErrorCodes.Exception`, as `GetSemiproductRecipePdfHandler` does. The brief's finding is scoped specifically to the two `InvalidOperationException` throws for business rules; adding blanket exception handling for infrastructure failures is a separate architectural decision not requested by the finding and is left out to keep this change surgical.

## Open Questions
None.

## Status: COMPLETE
