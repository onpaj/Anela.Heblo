# Implementation Plan: Structured error responses for GetManufactureProtocol

## Overview
Replace the two `throw new InvalidOperationException` business-rule failures in `GetManufactureProtocolHandler` with structured `GetManufactureProtocolResponse` error returns, add a new `ErrorCodes.ManufactureOrderNotCompleted = 1217`, and update `ManufactureOrderController.GetProtocolPdf` to delegate error handling to the inherited `HandleResponse` helper instead of catching exceptions — bringing this handler/action pair in line with every other action in the Manufacture module.

### task: structured-error-responses-for-protocol-generation

## Goal
Make `GetManufactureProtocolHandler.Handle` return structured `GetManufactureProtocolResponse` error results (instead of throwing) for "order not found" and "order not completed", add the new `ErrorCodes.ManufactureOrderNotCompleted` value, update `ManufactureOrderController.GetProtocolPdf` to use `HandleResponse`, and rewrite the four existing tests that assert today's throw-based behavior so the whole slice (handler + controller + tests) stays consistent and green in a single commit.

## Context

**Why:** `GetManufactureProtocolHandler` is currently the sole outlier in `ManufactureOrderController` that reports business-rule failures via `throw` + controller `catch`, instead of the `BaseResponse`/`ErrorCodes`/`HandleResponse` pattern used everywhere else in this module (e.g. `GetSemiproductRecipePdfHandler`, `UpdateManufactureOrderScheduleHandler`, `DuplicateManufactureOrderHandler`, and every other action in `ManufactureOrderController` such as `ResolveManualAction`). This is an architecture-review finding, not a functional bug — see `docs/architecture/development_guidelines.md` for the "business logic in handlers, not controllers" rule.

**Current handler code** (`GetManufactureProtocolHandler.cs`, lines 26-33):
```csharp
var order = await _repository.GetOrderByIdAsync(request.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Manufacture order {request.Id} not found.");

if (order.State != ManufactureOrderState.Completed)
{
    throw new InvalidOperationException(
        $"Manufacture order {order.OrderNumber} is not completed; cannot generate protocol.");
}
```

**Current controller code** (`ManufactureOrderController.cs`, lines 168-181):
```csharp
/// <summary>
/// Generate manufacture protocol PDF for a completed order
/// </summary>
[HttpGet("{id}/protocol.pdf")]
public async Task<IActionResult> GetProtocolPdf(int id, CancellationToken cancellationToken)
{
    var request = new GetManufactureProtocolRequest { Id = id };
    try
    {
        var response = await _mediator.Send(request, cancellationToken);
        return File(response.PdfBytes, "application/pdf", response.FileName);
    }
    catch (InvalidOperationException ex)
    {
        return BadRequest(new { message = ex.Message });
    }
}
```
Note there is no `[FeatureAuthorize]` method-level attribute on this action — auth is inherited from the class-level `[FeatureAuthorize(Feature.Manufacture_ManufactureOrders)]` on line 19 and must NOT be touched.

**`GetManufactureProtocolResponse`** (`GetManufactureProtocolResponse.cs`) already has everything needed — no changes required to this file:
```csharp
public class GetManufactureProtocolResponse : BaseResponse
{
    public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;

    public GetManufactureProtocolResponse() : base() { }                                            // Success = true
    public GetManufactureProtocolResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }                                                            // Success = false
}
```

**`ErrorCodes.cs`** Manufacture module (12XX) block currently ends at (lines 100-103):
```csharp
[HttpStatusCode(HttpStatusCode.NotFound)]
ManufacturedInventoryItemNotFound = 1215,
[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
ManufacturedInventoryInsufficientStock = 1216,

// Catalog module errors (13XX)
```
`ErrorCodes.OrderNotFound = 1210` (`[HttpStatusCode(HttpStatusCode.NotFound)]`, line 90-91) already exists and is reused as-is for the "order not found" case — do not add a new code for that condition.

`BaseApiController.HandleResponse<T>` (`BaseApiController.cs` line 28) has signature `protected ActionResult<T> HandleResponse<T>(T response) where T : BaseResponse` — it inspects `response.Success` and the `HttpStatusCodeAttribute` on the `ErrorCode` to pick `Ok`/`NotFound`/`BadRequest`/etc. No changes needed to this method.

**IMPORTANT — test-compilation gotcha not fully worked out in the spec/design:** changing the controller's return type from `Task<IActionResult>` to `Task<ActionResult<GetManufactureProtocolResponse>>` means the success-path `File(...)` call is implicitly wrapped by the `ActionResult<T>` conversion operator. The variable returned from the action is then of runtime type `ActionResult<GetManufactureProtocolResponse>`, **not** `FileContentResult` — the actual `FileContentResult` lives in its `.Result` property. The existing test `GetProtocolPdf_Should_Return_FileResult_With_Pdf_ContentType` currently asserts directly on the awaited result:
```csharp
var result = await _controller.GetProtocolPdf(orderId, CancellationToken.None);
var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
```
This assertion will fail once the signature changes (spec/design's claim that this test "passes unchanged" covers its *logic/expectations*, not its literal source — the accessor must change). Update it to unwrap via `.Result`:
```csharp
var result = await _controller.GetProtocolPdf(orderId, CancellationToken.None);
var fileResult = result.Result.Should().BeOfType<FileContentResult>().Subject;
```
Do not change any of the test's other assertions (content type, file name, PDF bytes, mediator verification) — only the accessor needed to reach the underlying `FileContentResult` given the new return type.

**Reference pattern already in this file** — `ResolveManualAction` (lines 152-163) shows the exact target shape for a `HandleResponse`-based action:
```csharp
[HttpPost("{id}/resolve-manual-action")]
[FeatureAuthorize(Feature.Manufacture_ManufactureOrders, AccessLevel.Write)]
public async Task<ActionResult<ResolveManualActionResponse>> ResolveManualAction(int id, [FromBody] ResolveManualActionRequest request)
{
    ...
    var response = await _mediator.Send(request);
    return HandleResponse(response);
}
```

## Files to create/modify
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs` — replace the two `throw new InvalidOperationException(...)` sites (lines 26-33) with early `return new GetManufactureProtocolResponse(...)` statements. No other change to this file (ERP document lookups, mapping, rendering all stay as-is).
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add `ManufactureOrderNotCompleted = 1217` to the 12XX block immediately after `ManufacturedInventoryInsufficientStock = 1216` (before the `// Catalog module errors (13XX)` comment), tagged `[HttpStatusCode(HttpStatusCode.BadRequest)]`.
- `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs` — change `GetProtocolPdf`'s signature from `Task<IActionResult>` to `Task<ActionResult<GetManufactureProtocolResponse>>`; remove the try/catch; branch on `response.Success`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs` — rewrite `Handle_NonCompletedOrder_Throws` and `Handle_OrderNotFound_Throws` to assert the structured-response contract (rename per FR-4/design). Leave all other tests (ERP document building, conditions-reading mapping, `Handle_CompletedOrder_ReturnsPdfWithCorrectFileName`, etc.) untouched.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderControllerProtocolTests.cs` — rewrite `GetProtocolPdf_Should_Return_BadRequest_When_Order_Not_Found` and `GetProtocolPdf_Should_Return_BadRequest_When_Order_Not_Completed` to mock a returned (not thrown) failed response and assert on HTTP status via result type. Update `GetProtocolPdf_Should_Return_FileResult_With_Pdf_ContentType`'s result-unwrapping accessor only (`result.Result.Should()...` instead of `result.Should()...`), per the gotcha above — its expectations (content type, file name, bytes, mediator verify) stay the same.

## Implementation steps
1. In `ErrorCodes.cs`, insert the new enum member after line 103 (`ManufacturedInventoryInsufficientStock = 1216,`) and before the `// Catalog module errors (13XX)` comment on line 105:
   ```csharp
   [HttpStatusCode(HttpStatusCode.BadRequest)]
   ManufactureOrderNotCompleted = 1217,
   ```
2. In `GetManufactureProtocolHandler.cs`, replace lines 26-33:
   ```csharp
   var order = await _repository.GetOrderByIdAsync(request.Id, cancellationToken);
   if (order == null)
   {
       return new GetManufactureProtocolResponse(
           ErrorCodes.OrderNotFound,
           new Dictionary<string, string> { { "orderId", request.Id.ToString() } });
   }

   if (order.State != ManufactureOrderState.Completed)
   {
       return new GetManufactureProtocolResponse(
           ErrorCodes.ManufactureOrderNotCompleted,
           new Dictionary<string, string> { { "orderId", order.OrderNumber }, { "state", order.State.ToString() } });
   }
   ```
   Confirm `Anela.Heblo.Application.Shared` (for `ErrorCodes`) is already reachable — it is, via the existing `Anela.Heblo.Domain.Features.Manufacture` and implicit project reference; add a `using Anela.Heblo.Application.Shared;` at the top of the file only if the build reports `ErrorCodes` as unresolved (it should already resolve since `GetManufactureProtocolResponse` in the same namespace references it, but the handler file itself has no such using today — verify during build).
3. In `ManufactureOrderController.cs`, replace the `GetProtocolPdf` action (lines 168-181):
   ```csharp
   /// <summary>
   /// Generate manufacture protocol PDF for a completed order
   /// </summary>
   [HttpGet("{id}/protocol.pdf")]
   public async Task<ActionResult<GetManufactureProtocolResponse>> GetProtocolPdf(int id, CancellationToken cancellationToken)
   {
       var request = new GetManufactureProtocolRequest { Id = id };
       var response = await _mediator.Send(request, cancellationToken);

       if (!response.Success)
       {
           return HandleResponse(response);
       }

       return File(response.PdfBytes, "application/pdf", response.FileName);
   }
   ```
   No method-level `[FeatureAuthorize]` change — the class-level attribute already covers this action.
4. In `GetManufactureProtocolHandlerTests.cs`, replace `Handle_OrderNotFound_Throws` and `Handle_NonCompletedOrder_Throws` per the "Tests to write" section below. Keep the existing `_repositoryMock` setup style (`ReturnsAsync`) — only the assertions change from `ThrowAsync<InvalidOperationException>` to direct result inspection (no `Should().ThrowAsync` needed since nothing throws now).
5. In `ManufactureOrderControllerProtocolTests.cs`:
   - Replace the mediator mock's `.ThrowsAsync(new InvalidOperationException(...))` in both `_Not_Completed` and `_Not_Found` tests with `.ReturnsAsync(new GetManufactureProtocolResponse(ErrorCodes.X, ...))`.
   - Update assertions to check the concrete result type through `.Result` (since the controller now returns `ActionResult<GetManufactureProtocolResponse>`): `result.Result.Should().BeOfType<NotFoundObjectResult>()` for not-found, `result.Result.Should().BeOfType<BadRequestObjectResult>()` for not-completed.
   - Update the untouched success test's unwrap line from `result.Should().BeOfType<FileContentResult>().Subject` to `result.Result.Should().BeOfType<FileContentResult>().Subject` (see gotcha above) — no other change to that test.
   - Add `using Anela.Heblo.Application.Shared;` to this test file if `ErrorCodes` is not already imported (check existing usings first).
6. Build and run the affected test projects; fix any compile errors surfaced by the signature change (e.g. any other caller of `GetProtocolPdf` in test/prod code — grep to confirm there are none beyond the two files listed).

## Tests to write
- `GetManufactureProtocolHandlerTests.Handle_OrderNotFound_Throws` → rename to `Handle_OrderNotFound_ReturnsErrorResponse` (or equivalent non-"Throws" name) — repository mock returns `null` for id 999; assert `result.Success == false` and `result.ErrorCode == ErrorCodes.OrderNotFound`; no exception thrown (drop the `Func<Task> act` / `ThrowAsync` pattern entirely, call `await _handler.Handle(...)` directly).
- `GetManufactureProtocolHandlerTests.Handle_NonCompletedOrder_Throws` → rename to `Handle_NonCompletedOrder_ReturnsErrorResponse` (or equivalent) — repository mock returns an order in `Planned` state; assert `result.Success == false` and `result.ErrorCode == ErrorCodes.ManufactureOrderNotCompleted`; no exception thrown.
- `ManufactureOrderControllerProtocolTests.GetProtocolPdf_Should_Return_BadRequest_When_Order_Not_Found` — mediator mock returns `new GetManufactureProtocolResponse(ErrorCodes.OrderNotFound, new Dictionary<string,string>{{"orderId","999"}})` (not `ThrowsAsync`); asserts `result.Result.Should().BeOfType<NotFoundObjectResult>()` (404, matching `OrderNotFound`'s `HttpStatusCodeAttribute`). Rename if desired to reflect "NotFound" rather than "BadRequest" (e.g. `GetProtocolPdf_Should_Return_NotFound_When_Order_Not_Found`) since the HTTP status is now genuinely 404 — keep the rename minimal and consistent with the file's existing naming style.
- `ManufactureOrderControllerProtocolTests.GetProtocolPdf_Should_Return_BadRequest_When_Order_Not_Completed` — mediator mock returns `new GetManufactureProtocolResponse(ErrorCodes.ManufactureOrderNotCompleted, new Dictionary<string,string>{{"orderId","MO-2024-001"},{"state","Planned"}})` (not `ThrowsAsync`); asserts `result.Result.Should().BeOfType<BadRequestObjectResult>()` (400).
- `ManufactureOrderControllerProtocolTests.GetProtocolPdf_Should_Return_FileResult_With_Pdf_ContentType` — unchanged expectations, only the unwrap accessor changes to `result.Result` (see gotcha above); still asserts content type `application/pdf`, correct file name, correct bytes, and that the mediator was called exactly once.
- All other existing tests in both files (ERP document building, conditions-reading mapping, other success-path assertions) must continue to pass unmodified — do not touch them.

## Acceptance criteria
- `GetManufactureProtocolHandler.Handle` no longer contains any `throw new InvalidOperationException`; both business-rule failures return a `GetManufactureProtocolResponse` with `Success == false` and the correct `ErrorCode` (`OrderNotFound` / `ManufactureOrderNotCompleted`).
- `ErrorCodes.ManufactureOrderNotCompleted = 1217` exists in `ErrorCodes.cs`, tagged `[HttpStatusCode(HttpStatusCode.BadRequest)]`, with no existing enum values renumbered or removed.
- `ManufactureOrderController.GetProtocolPdf` has signature `Task<ActionResult<GetManufactureProtocolResponse>>`, contains no try/catch, and delegates failure handling to `HandleResponse(response)`.
- `GET /api/ManufactureOrder/{id}/protocol.pdf` returns: 200 + PDF bytes (order found & completed, unchanged); 404 + `GetManufactureProtocolResponse` JSON with `ErrorCode: OrderNotFound` (order not found); 400 + `GetManufactureProtocolResponse` JSON with `ErrorCode: ManufactureOrderNotCompleted` (order found but not completed).
- `dotnet build` succeeds with no new warnings/errors introduced by this change.
- `dotnet format` produces no diffs on the touched files.
- All tests in `GetManufactureProtocolHandlerTests.cs` and `ManufactureOrderControllerProtocolTests.cs` pass, including the four rewritten tests and the untouched success-path tests.
- No other file in the repository references `GetProtocolPdf`'s old `IActionResult` return type or the removed exception-throwing behavior (grep `GetProtocolPdf` and `InvalidOperationException` scoped to this feature's files to confirm before finishing).
