# Convert Purchase Request DTOs From Record To Class Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert `GetPurchaseOrderByIdRequest`, `GetPurchaseOrderHistoryRequest`, and `UpdatePurchaseOrderStatusRequest` in the Purchase module from C# `record` to `class` (per `CLAUDE.md`'s "DTOs are classes, never C# records" rule), and update every construction call site — production and test — to object-initializer syntax.

**Architecture:** No structural change. Each of the three MediatR request types keeps its existing file, namespace, and `IRequest<TResponse>` contract; only the type declaration (`record(...)` → `class { get; set; }`) changes, matching the pattern already used by sibling types in this module (`CreatePurchaseOrderRequest`, `UpdatePurchaseOrderRequest`, `GetPurchaseOrdersRequest`, `GetPurchaseStockAnalysisRequest`). Split into two self-contained tasks so the solution builds and its tests pass at the end of each task: Task 1 covers the two `Get*` (single `Id` property) requests, Task 2 covers `UpdatePurchaseOrderStatusRequest` (two properties, higher-risk per the arch review) and its larger set of test call sites.

**Tech Stack:** .NET 8, MediatR, xUnit (test project `Anela.Heblo.Tests`).

---

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

### task: convert-update-purchase-order-status-request

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusRequest.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderStatusHandlerTests.cs` (14 occurrences)
- Modify: `backend/test/Anela.Heblo.Tests/Controllers/PurchaseOrdersControllerTests.cs` (4 occurrences)

`UpdatePurchaseOrderStatusRequest` has **no controller-side positional construction to update** — `PurchaseOrdersController.UpdatePurchaseOrderStatus` receives it via `[FromBody] UpdatePurchaseOrderStatusRequest request` (ASP.NET model binding), not `new UpdatePurchaseOrderStatusRequest(...)`. Only the DTO declaration and test call sites need to change.

- [ ] **Step 1: Confirm current call sites (baseline)**

Run:
```bash
grep -rn "new UpdatePurchaseOrderStatusRequest(" backend/
```
Expected: 18 lines total — 14 in `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderStatusHandlerTests.cs` (lines 42, 72, 99, 117, 138, 158, 183, 222, 243, 265, 287, 314, 342, 361) and 4 in `backend/test/Anela.Heblo.Tests/Controllers/PurchaseOrdersControllerTests.cs` (lines 332, 363, 373, 400). Confirm the controller source file (`PurchaseOrdersController.cs`) is NOT in the match list — if it is, the codebase has changed since this plan was written; stop and re-derive this task's file list before proceeding.

- [ ] **Step 2: Convert `UpdatePurchaseOrderStatusRequest` to a class**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusRequest.cs` with:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrderStatus;

public class UpdatePurchaseOrderStatusRequest : IRequest<UpdatePurchaseOrderStatusResponse>
{
    public int Id { get; set; }
    public string Status { get; set; } = null!;
}
```

- [ ] **Step 3: Update `UpdatePurchaseOrderStatusHandlerTests.cs` (14 occurrences)**

In `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderStatusHandlerTests.cs`, every occurrence follows the pattern `new UpdatePurchaseOrderStatusRequest(ValidOrderId, "<Status>");`. Replace each with `new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "<Status>" };`, preserving the exact status string per line. The verified original lines (by line number) and their status arguments are:

| Line | Original | Replacement |
|------|----------|--------------|
| 42 | `var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "InTransit");` | `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "InTransit" };` |
| 72 | `var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "Completed");` | `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "Completed" };` |
| 99 | `var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "InTransit");` | `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "InTransit" };` |
| 117 | `var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "InvalidStatus");` | `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "InvalidStatus" };` |
| 138 | `var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "Completed");` | `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "Completed" };` |
| 158 | `var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "InTransit");` | `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "InTransit" };` |
| 183 | `var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "InTransit");` | `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "InTransit" };` |
| 222 | `var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "InTransit");` | `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "InTransit" };` |
| 243 | `var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "InvalidStatus");` | `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "InvalidStatus" };` |
| 265 | `var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "Completed");` | `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "Completed" };` |
| 287 | `var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "Received");` | `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "Received" };` |
| 314 | `var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "Completed");` | `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "Completed" };` |
| 342 | `var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "Draft");` | `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "Draft" };` |
| 361 | `var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "Completed");` | `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "Completed" };` |

Since the pattern is identical (`var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "X");` → `var request = new UpdatePurchaseOrderStatusRequest { Id = ValidOrderId, Status = "X" };`) for all 14 lines, this can be done as a single regex-based replacement across the file — verify all 14 are converted (no more, no less) by re-running the Step 1 grep and confirming 0 matches remain in this file afterward.

- [ ] **Step 4: Update `PurchaseOrdersControllerTests.cs` (4 occurrences)**

In `backend/test/Anela.Heblo.Tests/Controllers/PurchaseOrdersControllerTests.cs`, the variable name is `statusRequest` rather than `request`:

| Line | Original | Replacement |
|------|----------|--------------|
| 332 | `var statusRequest = new UpdatePurchaseOrderStatusRequest(orderId, "InTransit");` | `var statusRequest = new UpdatePurchaseOrderStatusRequest { Id = orderId, Status = "InTransit" };` |
| 363 | `var statusRequest = new UpdatePurchaseOrderStatusRequest(orderId, "Completed");` | `var statusRequest = new UpdatePurchaseOrderStatusRequest { Id = orderId, Status = "Completed" };` |
| 373 | `var statusRequest = new UpdatePurchaseOrderStatusRequest(nonExistentId, "InTransit");` | `var statusRequest = new UpdatePurchaseOrderStatusRequest { Id = nonExistentId, Status = "InTransit" };` |
| 400 | `var statusRequest = new UpdatePurchaseOrderStatusRequest(orderId, "InTransit");` | `var statusRequest = new UpdatePurchaseOrderStatusRequest { Id = orderId, Status = "InTransit" };` |

- [ ] **Step 5: Build and verify no remaining positional construction anywhere in the repo**

Run:
```bash
grep -rn "new GetPurchaseOrderByIdRequest(\|new GetPurchaseOrderHistoryRequest(\|new UpdatePurchaseOrderStatusRequest(" backend/ | grep -v "Request {"
```
Expected: no output. This confirms both this task's and the previous task's conversions are complete and no positional-construction call site was missed anywhere in `backend/`.

Run:
```bash
cd backend && dotnet build
```
Expected: `Build succeeded.` with no new errors or warnings.

Run:
```bash
cd backend && dotnet format --verify-no-changes
```
Expected: no formatting violations (per this repo's validation-before-completion rule). If it reports changes, run `dotnet format` and re-verify.

- [ ] **Step 6: Run the full affected test suite**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~UpdatePurchaseOrderStatusHandlerTests|FullyQualifiedName~PurchaseOrdersControllerTests|FullyQualifiedName~GetPurchaseOrderHistoryHandlerTests"
```
Expected: all tests pass.

Then run the complete backend test project once to confirm nothing else in the solution was affected:
```bash
cd backend && dotnet test
```
Expected: all tests pass, 0 failures.

- [ ] **Step 7: Commit**

```bash
cd backend && git add \
  src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusRequest.cs \
  test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderStatusHandlerTests.cs \
  test/Anela.Heblo.Tests/Controllers/PurchaseOrdersControllerTests.cs
git commit -m "refactor(purchase): convert UpdatePurchaseOrderStatusRequest from record to class"
```

---

## Final Validation (run once, after both tasks)

- [ ] `cd backend && dotnet build` — `Build succeeded.`, no new warnings.
- [ ] `cd backend && dotnet format --verify-no-changes` — clean.
- [ ] `cd backend && dotnet test` — full backend suite passes.
- [ ] `grep -rn "new GetPurchaseOrderByIdRequest(\|new GetPurchaseOrderHistoryRequest(\|new UpdatePurchaseOrderStatusRequest(" backend/ | grep -v "Request {"` — no output.
- [ ] Frontend is unaffected by this change (no route/JSON shape change), but per this repo's rule that the OpenAPI TypeScript client is auto-generated on build, run `cd frontend && npm run build` once to confirm the client regenerates cleanly and `npm run lint` passes.
