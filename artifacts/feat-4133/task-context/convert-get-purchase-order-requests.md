### task: convert-get-purchase-order-requests

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryRequest.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs` (lines ~65, ~140)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseOrderHistoryHandlerTests.cs` (lines 32, 52, 75, 98)

This task converts the two single-property request types (`GetPurchaseOrderByIdRequest`, `GetPurchaseOrderHistoryRequest`) and every place that constructs them positionally. `GetPurchaseOrderByIdRequest` has no existing dedicated handler test file to update (verified: no positional-construction matches for it outside the DTO and controller files).

- [ ] **Step 1: Confirm current call sites (baseline)**

Run:
```bash
grep -rn "new GetPurchaseOrderByIdRequest(\|new GetPurchaseOrderHistoryRequest(" backend/
```
Expected output (4 lines, confirming the full set of call sites this task must update):
```
backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs:65:        var request = new GetPurchaseOrderByIdRequest(id);
backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs:140:        var response = await _mediator.Send(new GetPurchaseOrderHistoryRequest(id), cancellationToken);
backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseOrderHistoryHandlerTests.cs:32:        var response = await _handler.Handle(new GetPurchaseOrderHistoryRequest(missingId), CancellationToken.None);
backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseOrderHistoryHandlerTests.cs:52:        var response = await _handler.Handle(new GetPurchaseOrderHistoryRequest(orderId), CancellationToken.None);
```
(Note: grep with `-n` also matches lines 75 and 98 of `GetPurchaseOrderHistoryHandlerTests.cs` — 4 occurrences total in that file. List all of them before proceeding.)

- [ ] **Step 2: Convert `GetPurchaseOrderByIdRequest` to a class**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdRequest.cs` with:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderById;

public class GetPurchaseOrderByIdRequest : IRequest<GetPurchaseOrderByIdResponse>
{
    public int Id { get; set; }
}
```

- [ ] **Step 3: Convert `GetPurchaseOrderHistoryRequest` to a class**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryRequest.cs` with:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderHistory;

public class GetPurchaseOrderHistoryRequest : IRequest<ListResponse<PurchaseOrderHistoryDto>>
{
    public int Id { get; set; }
}
```

- [ ] **Step 4: Update the controller call sites**

In `backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs`, change:

```csharp
        var request = new GetPurchaseOrderByIdRequest(id);
```
to:
```csharp
        var request = new GetPurchaseOrderByIdRequest { Id = id };
```

And change:
```csharp
        var response = await _mediator.Send(new GetPurchaseOrderHistoryRequest(id), cancellationToken);
```
to:
```csharp
        var response = await _mediator.Send(new GetPurchaseOrderHistoryRequest { Id = id }, cancellationToken);
```

- [ ] **Step 5: Update the test call sites**

In `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseOrderHistoryHandlerTests.cs`, change each of the following 4 lines (verified exact original text):

Line 32:
```csharp
        var response = await _handler.Handle(new GetPurchaseOrderHistoryRequest(missingId), CancellationToken.None);
```
→
```csharp
        var response = await _handler.Handle(new GetPurchaseOrderHistoryRequest { Id = missingId }, CancellationToken.None);
```

Lines 52 and 75 (identical text, both occurrences):
```csharp
        var response = await _handler.Handle(new GetPurchaseOrderHistoryRequest(orderId), CancellationToken.None);
```
→
```csharp
        var response = await _handler.Handle(new GetPurchaseOrderHistoryRequest { Id = orderId }, CancellationToken.None);
```

Line 98:
```csharp
        await _handler.Handle(new GetPurchaseOrderHistoryRequest(orderId), CancellationToken.None);
```
→
```csharp
        await _handler.Handle(new GetPurchaseOrderHistoryRequest { Id = orderId }, CancellationToken.None);
```

(If line numbers have drifted from prior edits in the same session, search for the exact original text shown above rather than trusting the line number.)

- [ ] **Step 6: Build and verify no remaining positional construction**

Run:
```bash
grep -rn "new GetPurchaseOrderByIdRequest(\|new GetPurchaseOrderHistoryRequest(" backend/ | grep -v "Request {"
```
Expected: no output (every remaining match must be the `Request { Id = ... }` object-initializer form, which the `grep -v` filters out).

Run:
```bash
cd backend && dotnet build
```
Expected: `Build succeeded.` with no new errors or warnings.

- [ ] **Step 7: Run the affected tests**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetPurchaseOrderHistoryHandlerTests|FullyQualifiedName~PurchaseOrdersControllerTests"
```
Expected: all tests pass (the `GetPurchaseOrderById` route is also covered indirectly by `PurchaseOrdersControllerTests` — include it in this run to catch any regression from Step 4).

- [ ] **Step 8: Commit**

```bash
cd backend && git add \
  src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdRequest.cs \
  src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryRequest.cs \
  src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs \
  test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseOrderHistoryHandlerTests.cs
git commit -m "refactor(purchase): convert GetPurchaseOrderById/HistoryRequest from record to class"
```

---

