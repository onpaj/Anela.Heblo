# Design: Structured error responses for GetManufactureProtocol

## Component Design

No new components are introduced. This is an in-place conformance fix to two existing components in the Manufacture vertical slice, bringing them in line with the `BaseResponse`/`ErrorCodes`/`HandleResponse` pattern already used by every other handler/action pair in the module.

### `GetManufactureProtocolHandler` (MediatR handler)
`backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs`

- **Responsibility (unchanged):** load the manufacture order, validate it is eligible for protocol generation, build `ManufactureProtocolData`, render the PDF via `IManufactureProtocolRenderer`.
- **Contract change:** the two business-rule failure paths become early returns instead of throws:
  - `_repository.GetOrderByIdAsync` returns `null` → `return new GetManufactureProtocolResponse(ErrorCodes.OrderNotFound, new Dictionary<string, string> { { "orderId", request.Id.ToString() } });`
  - `order.State != ManufactureOrderState.Completed` → `return new GetManufactureProtocolResponse(ErrorCodes.ManufactureOrderNotCompleted, new Dictionary<string, string> { { "orderId", order.OrderNumber }, { "state", order.State.ToString() } });`
  - Success path is unchanged: builds and returns `GetManufactureProtocolResponse` with `PdfBytes`/`FileName` populated (`Success = true` via parameterless base constructor).
- No changes to ERP document lookups, data mapping, or rendering logic (out of scope).
- No changes to `GetManufactureProtocolResponse` — its `(ErrorCodes, Dictionary<string,string>?)` constructor already exists; this handler becomes its first caller.

### `ManufactureOrderController.GetProtocolPdf` (MVC action)
`backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs`

- **Responsibility (unchanged):** HTTP entry point for `GET /api/ManufactureOrder/{id}/protocol.pdf`; sends `GetManufactureProtocolRequest` via `IMediator`.
- **Contract change:** signature changes from `Task<IActionResult>` to `Task<ActionResult<GetManufactureProtocolResponse>>`, matching every other action in the controller. The try/catch around `InvalidOperationException` is removed entirely.
  - `response.Success == true` → `return File(response.PdfBytes, "application/pdf", response.FileName);` (`FileContentResult` implicitly converts to `ActionResult<GetManufactureProtocolResponse>`).
  - `response.Success == false` → `return HandleResponse(response);` (inherited from `BaseApiController`, no manual status-code construction).
- Auth is unaffected: class-level `[FeatureAuthorize(Feature.Manufacture_ManufactureOrders)]` continues to cover this action with no method-level override.

### `ErrorCodes` enum
`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`

- New member added to the existing 12XX (Manufacture) block, immediately after `ManufacturedInventoryInsufficientStock = 1216`:
  ```csharp
  [HttpStatusCode(HttpStatusCode.BadRequest)]
  ManufactureOrderNotCompleted = 1217,
  ```
- Reuses existing `OrderNotFound = 1210` (`[HttpStatusCode(HttpStatusCode.NotFound)]`) for the "order not found" case — no new code needed for that path.
- No existing values renumbered or removed.

### Data flow (unchanged from architecture review)

```
ManufactureOrderController.GetProtocolPdf (HTTP layer)
        │  sends GetManufactureProtocolRequest via IMediator
        ▼
GetManufactureProtocolHandler.Handle (MediatR handler, business rules)
        │  returns GetManufactureProtocolResponse (BaseResponse: Success/ErrorCode/Params)
        ▼
ManufactureOrderController.GetProtocolPdf
        │  response.Success == true  → File(pdfBytes, "application/pdf", fileName)
        │  response.Success == false → HandleResponse(response)  [inherited from BaseApiController]
        ▼
BaseApiController.HandleResponse<T> → reflects HttpStatusCodeAttribute on ErrorCode → 404 / 400 / etc.
```

### Test surface

- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs`
  - `Handle_OrderNotFound_Throws` → replaced with an assertion-based test: `result.Success == false`, `result.ErrorCode == ErrorCodes.OrderNotFound` (no exception expected).
  - `Handle_NonCompletedOrder_Throws` → replaced with: `result.Success == false`, `result.ErrorCode == ErrorCodes.ManufactureOrderNotCompleted` (no exception expected).
  - All ERP-document-building / mapping success-path tests remain unmodified.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderControllerProtocolTests.cs`
  - `GetProtocolPdf_Should_Return_BadRequest_When_Order_Not_Found` → mediator mock now returns a failed `GetManufactureProtocolResponse(ErrorCodes.OrderNotFound, ...)` (not `ThrowsAsync`); asserts `NotFoundObjectResult` (404).
  - `GetProtocolPdf_Should_Return_BadRequest_When_Order_Not_Completed` → mediator mock now returns a failed `GetManufactureProtocolResponse(ErrorCodes.ManufactureOrderNotCompleted, ...)` (not `ThrowsAsync`); asserts `BadRequestObjectResult` (400).
  - `GetProtocolPdf_Should_Return_FileResult_With_Pdf_ContentType` remains unmodified (success path untouched).

## Data Schemas

### `GetManufactureProtocolResponse` (no code change — documented for reference)
```csharp
public class GetManufactureProtocolResponse : BaseResponse
{
    public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;

    // GetManufactureProtocolResponse() : base()
    //   -> Success = true (used on the happy path)
    // GetManufactureProtocolResponse(ErrorCodes errorCode, Dictionary<string,string>? parameters = null) : base(errorCode, parameters)
    //   -> Success = false (used on both error paths)
}
```

### `ErrorCodes` addition
```csharp
// backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs, 12XX (Manufacture) block
...
[HttpStatusCode(HttpStatusCode.BadRequest)]
ManufacturedInventoryInsufficientStock = 1216,

[HttpStatusCode(HttpStatusCode.BadRequest)]
ManufactureOrderNotCompleted = 1217,   // new
...
```

### HTTP API: `GET /api/ManufactureOrder/{id}/protocol.pdf`

| Condition | Status | Body |
|---|---|---|
| Order found, `State == Completed` | 200 | `application/pdf` binary (byte-identical to current behavior) |
| Order not found | 404 (was 400) | `GetManufactureProtocolResponse` JSON: `{ "success": false, "errorCode": "OrderNotFound", "params": { "orderId": "{id}" } }` |
| Order found, `State != Completed` | 400 (unchanged status) | `GetManufactureProtocolResponse` JSON: `{ "success": false, "errorCode": "ManufactureOrderNotCompleted", "params": { "orderId": "{orderNumber}", "state": "{state}" } }` |

Note the intentional asymmetry in the `orderId` param value, carried over from the current code and confirmed correct by the architecture review: the not-found case uses `request.Id.ToString()` (numeric id, since no `order` was loaded), while the not-completed case uses `order.OrderNumber` (human-readable order number, since the order was successfully loaded).

This is a breaking response-shape change for the two error branches only (freeform `{ "message": string }` → structured `GetManufactureProtocolResponse` with `ErrorCode`/`Params`; not-found status changes 400 → 404). The success (200, PDF) branch is byte-for-byte unchanged. No persistent/database schema changes; no new events.
