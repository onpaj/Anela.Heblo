# Architecture Review: Structured error responses for GetManufactureProtocol

## Skip Design: true

## Architectural Fit Assessment
This is a pure conformance fix, not new architecture. `GetManufactureProtocolHandler` is the sole outlier in `ManufactureOrderController` that reports business-rule failures via `throw` + controller `catch`, instead of the `BaseResponse`/`ErrorCodes`/`HandleResponse` pattern used everywhere else in this module and codebase-wide. The spec's investigation is verified correct against the actual source:

- `GetManufactureProtocolHandler.cs:26-33` does throw `InvalidOperationException` twice, confirmed by direct read.
- `GetManufactureProtocolResponse` already inherits `BaseResponse` and already exposes the `(ErrorCodes, Dictionary<string,string>?)` constructor — no response-shape change needed, confirmed by direct read.
- `ManufactureOrderController.GetProtocolPdf` (lines 169-181) does exactly the try/catch-and-translate the spec describes, confirmed by direct read.
- `ErrorCodes.OrderNotFound = 1210` (`[HttpStatusCode(HttpStatusCode.NotFound)]`) already exists in the 12XX Manufacture block and is reusable as-is, confirmed by direct read of `ErrorCodes.cs`.
- `ManufacturedInventoryInsufficientStock = 1216` is the last member of the 12XX block, confirmed by direct read — `1217` is genuinely the next free value in that block (no collision with `1301` Catalog block, which starts fresh).
- `BaseApiController.HandleResponse<T>` (lines 28-59) confirmed: reflects `HttpStatusCodeAttribute` off the `ErrorCodes` enum member and switches on the resolved `HttpStatusCode`, returning `Ok`/`BadRequest`/`NotFound`/etc. — no bespoke logic needed for the new code, `BadRequest` is the `_ => StatusCode(...)`-independent default path already handled.
- `GetSemiproductRecipePdfHandler.cs` confirmed as a valid reference for a PDF-returning handler that never throws for business conditions and returns `new XxxResponse(ErrorCodes.Y, params)` on early-exit — the proposed `GetManufactureProtocolHandler` change follows the same shape (the spec correctly scopes the catch-all `try/catch(Exception)` wrapper as out-of-scope, since the brief's finding is specifically about the two business-rule throws).
- Class-level `[FeatureAuthorize(Feature.Manufacture_ManufactureOrders)]` on `ManufactureOrderController` (line 19) confirmed to already cover `GetProtocolPdf` with no method-level override — the spec's claim of "no auth change" holds.
- Both test files (`GetManufactureProtocolHandlerTests.cs`, `ManufactureOrderControllerProtocolTests.cs`) confirmed to contain exactly the throw/catch-asserting tests the spec lists for rewrite, with no other latent assertions on the old contract.

No new components, no new module, no new external dependency. This is a same-file, same-pattern refactor entirely inside the existing Manufacture vertical slice — it does not touch module boundaries, `Contracts/` folders, DI registration, or persistence.

## Proposed Architecture

### Component Overview
No new components. The two touched components already exist and keep their existing roles:

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

### Key Design Decisions

#### Decision 1: Reuse `ErrorCodes.OrderNotFound` vs. introduce a new "not found" code
**Options considered:** (a) generic `ErrorCodes.ResourceNotFound` (0006) as the brief suggested; (b) module-specific `ErrorCodes.OrderNotFound` (1210), already used by `DuplicateManufactureOrderHandler` for the identical condition.
**Chosen approach:** (b) — reuse `OrderNotFound = 1210`.
**Rationale:** The Manufacture module already has a specific code for "manufacture order id not found," in active use elsewhere in the same controller's request family. Introducing a second, generic code for the same condition would fragment the error taxonomy and give API consumers two different `ErrorCode` values for what is semantically the same failure. This is a correction to the brief, not a deviation from it — the brief's suggested fix was illustrative, not binding.

#### Decision 2: New error code for "order not completed" vs. reusing an existing `CannotUpdate*` code
**Options considered:** (a) reuse `CannotUpdateCompletedOrder` (1211) or `CannotUpdateCancelledOrder` (1212); (b) add a new `ManufactureOrderNotCompleted = 1217`.
**Chosen approach:** (b).
**Rationale:** `CannotUpdateCompletedOrder`/`CannotUpdateCancelledOrder` describe the inverse condition (order already completed, blocking a *mutation*). Reusing either here would report a misleading `ErrorCode` string to API consumers for a *read* operation blocked because the order is *not yet* completed. A dedicated code keeps each `ErrorCodes` member meaning one thing, consistent with every other module's convention observed in `ErrorCodes.cs` (each condition gets its own value; no polysemous reuse elsewhere in the file).

#### Decision 3: Controller signature change (`IActionResult` → `ActionResult<GetManufactureProtocolResponse>`)
**Options considered:** (a) keep `Task<IActionResult>` and manually branch `if (!response.Success) return StatusCode(...)`; (b) change the return type to `Task<ActionResult<GetManufactureProtocolResponse>>` and delegate fully to `HandleResponse`.
**Chosen approach:** (b).
**Rationale:** Every other action in `ManufactureOrderController` (`GetOrder`, `CreateOrder`, `UpdateOrderSchedule`, etc.) already uses `Task<ActionResult<TResponse>>` + `HandleResponse`. `FileContentResult` (returned by `File(...)`) derives from `ActionResult`, which has an implicit conversion to `ActionResult<T>`, so the success path compiles unchanged. Keeping `IActionResult` here would leave this one action as a second convention inside the same controller for no benefit.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. All edits are in-place:
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs` — replace the two `throw` sites with early `return new GetManufactureProtocolResponse(ErrorCodes.X, params)`.
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add `ManufactureOrderNotCompleted = 1217` to the 12XX block, immediately after `ManufacturedInventoryInsufficientStock = 1216`, tagged `[HttpStatusCode(HttpStatusCode.BadRequest)]`.
- `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs` — change `GetProtocolPdf`'s return type and body per Decision 3; remove the try/catch.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs` — rewrite the two throw-asserting tests; leave all other tests (ERP document building, mapping) untouched.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderControllerProtocolTests.cs` — rewrite the two throw-asserting tests; leave `GetProtocolPdf_Should_Return_FileResult_With_Pdf_ContentType` untouched.

`GetManufactureProtocolResponse.cs` needs **no edit** — its error-path constructor already exists and is unused today; this change is the first caller of it.

### Interfaces and Contracts
No new interfaces. `GetManufactureProtocolResponse` (unchanged) is the only contract surface:
```csharp
public class GetManufactureProtocolResponse : BaseResponse
{
    public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
    // () : base()                                            -- Success = true
    // (ErrorCodes, Dictionary<string,string>?) : base(...)   -- Success = false
}
```
`ErrorCodes.ManufactureOrderNotCompleted = 1217` is the one new contract member (see Data Model in spec — this review has no amendments to it).

### Data Flow
Unchanged from the spec's API table — reaffirmed correct against source:

| Condition | Response |
|---|---|
| Found, `State == Completed` | 200, `application/pdf` binary (byte-identical to today) |
| Not found | 404, JSON body, `ErrorCode: OrderNotFound` (1210), `Params: { orderId }` |
| Found, `State != Completed` | 400, JSON body, `ErrorCode: ManufactureOrderNotCompleted` (1217), `Params: { orderId, state }` |

This is a breaking shape/status change for the two error branches (400→404 for not-found; freeform `message` string → structured `ErrorCode`/`Params`). Confirmed via source read: this endpoint's only current consumer in-repo appears to be manual/browser download (no frontend TypeScript client call to `.../protocol.pdf` error-branch handling was located in this review's spot-check — this matches the spec's "no known frontend consumer" claim, but was not exhaustively re-verified across the whole `frontend/` tree by this review; treat as inherited from spec, not independently re-confirmed).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A frontend caller silently depended on the old 400-with-`message` shape for the not-found case (now 404, structured body) | Low | Spec already flags this as out-of-scope with no consumer found; implementer should grep `frontend/` for calls to `/protocol.pdf` (e.g. `protocol.pdf`, `GetProtocolPdf`) as a final check before merging, not just trust the spec's claim |
| `1217` collides with a value added by a concurrent branch also extending the 12XX block | Low | Single-file enum edit, solo-developer repo per CLAUDE.md; `dotnet build` will not catch a duplicate numeric value silently reused (C# allows duplicate enum values) — reviewer should visually confirm `1217` is still unused at merge time, not just at spec time |
| `HandleResponse`'s `NotFound(response)`/`BadRequest(response)` wrap the full `GetManufactureProtocolResponse` (including empty `PdfBytes`/`FileName`) in the JSON error body, slightly heavier than a minimal error DTO | Negligible | This is the existing, established pattern for every other action in this controller (e.g. `GetSemiproductRecipePdfResponse` on its error path) — consistent, not a new cost introduced by this change |

## Specification Amendments
None required — the spec's FR-1 through FR-4, error code choices (reuse `OrderNotFound`, add `ManufactureOrderNotCompleted = 1217`), and test-rewrite list were all independently verified against the current source in this review and are correct. This review confirms rather than amends.

One clarification for the implementer, not a spec change: when constructing the "not completed" response, use `order.OrderNumber` for the `orderId` param key (matching FR-1's acceptance criterion and the old exception message's use of `order.OrderNumber`), and `request.Id.ToString()` for the not-found case's `orderId` param key (matching FR-1, since no `order` is available). Keep the two `orderId` values named consistently even though one is the numeric `Id` and the other is the human `OrderNumber` — this asymmetry already exists in the spec and mirrors the current code's own inconsistency (`request.Id` vs. `order.OrderNumber`), so no new inconsistency is introduced.

## Prerequisites
None. No migrations, no config, no infrastructure changes — this is a same-commit, same-slice code change ready to implement directly against `main`/the current branch.
