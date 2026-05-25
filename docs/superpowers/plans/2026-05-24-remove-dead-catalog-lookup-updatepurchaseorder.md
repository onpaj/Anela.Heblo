# Remove Dead Catalog Lookup in UpdatePurchaseOrderHandler — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the dead, per-line `_catalogRepository.GetByIdAsync` call inside `UpdatePurchaseOrderHandler.MapToResponseAsync` whose result is never used, and convert the now-await-free method to a synchronous helper without changing any observable behavior of `PUT /api/purchase-orders/{id}`.

**Architecture:** Single-file edit in a MediatR handler under Vertical Slice `Features/Purchase/UseCases/UpdatePurchaseOrder/`. The fix is pure dead-code removal — no new interfaces, no DI changes, no contract changes. `ICatalogRepository` stays as a dependency because it is still legitimately consumed in `Handle` (lines 80 and 93) to compute `materialName` for `UpdateLine`/`AddLine`. The response DTO already sourced `MaterialName` from the entity (`line.MaterialName`), so deleting the unused lookup leaves the response byte-identical.

**Tech Stack:** .NET 8, C#, MediatR, xUnit + Moq + FluentAssertions for tests, EF Core (via `IPurchaseOrderRepository`).

---

## File Structure

**Modified files:**
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs` — remove dead lines inside `MapToResponseAsync`, convert it to a synchronous `MapToResponse`, update the single call site.

**Created files:**
- `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderHandlerTests.cs` — new test file with a focused regression test that proves `MapToResponse` does **not** invoke `ICatalogRepository.GetByIdAsync` on the response-mapping path. Mirrors the structure of the sibling `CreatePurchaseOrderHandlerTests.cs` for consistency.

**Out-of-scope (verified by grep, do not touch):**
- `CreatePurchaseOrderHandler.cs:79` — catalog lookup result is used in `AddLine`. Not dead.
- `GetPurchaseOrderByIdHandler.cs:54` — catalog lookup feeds `CatalogNote` (catalog-only field). Legitimate.
- `UpdatePurchaseOrderHandler.cs:80` and `:93` — catalog lookup result is used by `UpdateLine`/`AddLine`. These are an adjacent N+1 with a *used* result; they are a separate ticket per the arch review and **must not** be changed here.

---

## Task 1: Add regression test that proves `MapToResponse` never calls the catalog

We add the test **first** (TDD red step) so that we have a guarantee that the post-edit behavior really removes the unused call.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderHandlerTests.cs`

### Step 1.1: Write the failing test

- [ ] **Step 1.1:** Create the test file with a single test that exercises the update flow with one existing line and one new line, then asserts that the catalog repository is invoked **at most twice** (once for the `UpdateLine` branch at handler line 80, once for the `AddLine` branch at handler line 93) — i.e. **never** invoked a third time during response mapping.

Create `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderHandlerTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class UpdatePurchaseOrderHandlerTests
{
    private readonly Mock<ILogger<UpdatePurchaseOrderHandler>> _loggerMock = new();
    private readonly Mock<IPurchaseOrderRepository> _repositoryMock = new();
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<ISupplierRepository> _supplierRepositoryMock = new();
    private readonly UpdatePurchaseOrderHandler _handler;

    private const long ValidSupplierId = 1;
    private const string ValidSupplierName = "Test Supplier";
    private const string ExistingMaterialId = "MAT-EXISTING";
    private const string NewMaterialId = "MAT-NEW";

    public UpdatePurchaseOrderHandlerTests()
    {
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", "Test User", "test@example.com", true));

        _supplierRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidSupplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Supplier { Id = ValidSupplierId, Name = ValidSupplierName, Code = "SUP001" });

        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => new CatalogAggregate { ProductCode = id, ProductName = $"Material {id}" });

        _handler = new UpdatePurchaseOrderHandler(
            _loggerMock.Object,
            _repositoryMock.Object,
            _catalogRepositoryMock.Object,
            _currentUserServiceMock.Object,
            _supplierRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WithUpdateAndAddLines_DoesNotCallCatalogDuringResponseMapping()
    {
        // Arrange — seed an order that has one existing line.
        var order = new PurchaseOrder(
            orderNumber: "PO-2024-001",
            supplierId: ValidSupplierId,
            supplierName: ValidSupplierName,
            orderDate: new DateTime(2024, 8, 2),
            expectedDeliveryDate: new DateTime(2024, 8, 16),
            contactVia: null,
            notes: null,
            createdBy: "seed");
        order.AddLine(ExistingMaterialId, "Existing Material", quantity: 1m, unitPrice: 10m, notes: null);
        var existingLineId = order.Lines.Single().Id;

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var request = new UpdatePurchaseOrderRequest
        {
            Id = order.Id,
            SupplierId = ValidSupplierId,
            OrderNumber = order.OrderNumber,
            ExpectedDeliveryDate = order.ExpectedDeliveryDate,
            ContactVia = null,
            Notes = null,
            Lines = new List<UpdatePurchaseOrderLineRequestDto>
            {
                new()
                {
                    Id = existingLineId,
                    MaterialId = ExistingMaterialId,
                    Name = "Existing Material",
                    Quantity = 2m,
                    UnitPrice = 10m,
                    Notes = null,
                },
                new()
                {
                    Id = null,
                    MaterialId = NewMaterialId,
                    Name = "New Material",
                    Quantity = 3m,
                    UnitPrice = 20m,
                    Notes = null,
                },
            },
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert — response is well-formed and MaterialName comes from the entity.
        response.Success.Should().BeTrue();
        response.Lines.Should().HaveCount(2);
        response.Lines.Should().OnlyContain(line => !string.IsNullOrEmpty(line.MaterialName));

        // Assert — catalog is hit exactly twice (once per request line, inside Handle),
        // and never again inside MapToResponse. This is the regression guard.
        _catalogRepositoryMock.Verify(
            x => x.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
```

> **Note for the implementer:** Property names on `UpdatePurchaseOrderRequest`, `UpdatePurchaseOrderLineRequestDto`, `PurchaseOrder`, and `CatalogAggregate` shown above must match the codebase exactly. If a property does not exist with the spelling shown (e.g. `Name` on the line request, or `ProductName`/`ProductCode` on `CatalogAggregate`), open the type and use the actual property name — do not invent fields. The intent of the test is to: (1) drive the handler through both the update-existing-line and add-new-line branches, and (2) verify `_catalogRepositoryMock.GetByIdAsync` is called exactly twice (i.e. zero times during response mapping). Adjust property spellings to match the real types; do not change the assertion semantics.

### Step 1.2: Run the test to confirm it fails

- [ ] **Step 1.2:** Run only the new test. It must fail with a Moq `Times.Exactly(2)` assertion failure showing **3** invocations of `_catalogRepository.GetByIdAsync` (two from `Handle` lines 80/93, plus one from `MapToResponseAsync` line 128).

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdatePurchaseOrderHandlerTests"
```

Expected: **FAIL** with FluentAssertions/Moq output indicating the mock was invoked 3 times when 2 were expected.

If the test instead fails at the Arrange stage (e.g. with `ArgumentException` or `NullReferenceException`) because property names on the seed types do not match the codebase, **fix the property names in the test only** (do not change the handler), re-run, and confirm the failure mode is the catalog call-count mismatch before proceeding.

### Step 1.3: Commit the failing test

- [ ] **Step 1.3:** Commit the failing test as a guardrail before the source edit.

```bash
git add backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderHandlerTests.cs
git commit -m "test(purchase): add regression for unused catalog lookup in UpdatePurchaseOrder mapping"
```

---

## Task 2: Remove the dead lookup and convert `MapToResponseAsync` to a synchronous helper

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs:110` (single call site)
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs:121-142` (method signature and body)

### Step 2.1: Delete the dead `material` / `materialName` lines inside `MapToResponseAsync`

- [ ] **Step 2.1:** Remove the comment and two unused variable declarations inside the `foreach (var line in purchaseOrder.Lines)` loop. The `lines.Add(new PurchaseOrderLineDto { … })` block stays exactly as-is and still reads `MaterialName = line.MaterialName` from the entity.

In `UpdatePurchaseOrderHandler.cs`, replace:

```csharp
        foreach (var line in purchaseOrder.Lines)
        {
            // Try to get material name from catalog
            var material = await _catalogRepository.GetByIdAsync(line.MaterialId, cancellationToken);
            var materialName = material?.ProductName ?? "Unknown Material";

            lines.Add(new PurchaseOrderLineDto
            {
                Id = line.Id,
                MaterialId = line.MaterialId,
                Code = line.MaterialId, // Code is same as MaterialId
                MaterialName = line.MaterialName,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineTotal = line.LineTotal,
                Notes = line.Notes
            });
        }
```

with:

```csharp
        foreach (var line in purchaseOrder.Lines)
        {
            lines.Add(new PurchaseOrderLineDto
            {
                Id = line.Id,
                MaterialId = line.MaterialId,
                Code = line.MaterialId, // Code is same as MaterialId
                MaterialName = line.MaterialName,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineTotal = line.LineTotal,
                Notes = line.Notes
            });
        }
```

### Step 2.2: Convert `MapToResponseAsync` to synchronous `MapToResponse`

- [ ] **Step 2.2:** The method body now contains no `await`. To avoid the CS1998 "async method lacks await" warning (NFR-2: no new warnings) without papering over it with `await Task.CompletedTask`, drop `async`, drop the `Task<…>` wrapper, drop the unused `CancellationToken cancellationToken` parameter, and rename `MapToResponseAsync` → `MapToResponse`.

Change the method signature from:

```csharp
    private async Task<UpdatePurchaseOrderResponse> MapToResponseAsync(PurchaseOrder purchaseOrder, long supplierId, CancellationToken cancellationToken)
    {
```

to:

```csharp
    private UpdatePurchaseOrderResponse MapToResponse(PurchaseOrder purchaseOrder, long supplierId)
    {
```

The body (the `var lines = new List<PurchaseOrderLineDto>(); foreach …; return new UpdatePurchaseOrderResponse { … };`) stays exactly as edited in Step 2.1.

### Step 2.3: Update the single call site

- [ ] **Step 2.3:** Update the caller at `UpdatePurchaseOrderHandler.cs:110` to drop the `await` and the trailing `cancellationToken` argument, and to use the new method name.

Change:

```csharp
            return await MapToResponseAsync(purchaseOrder, request.SupplierId, cancellationToken);
```

to:

```csharp
            return MapToResponse(purchaseOrder, request.SupplierId);
```

### Step 2.4: Verify `_catalogRepository`, the constructor parameter, and the `using` directive are unchanged

- [ ] **Step 2.4:** Open `UpdatePurchaseOrderHandler.cs` and confirm — **without making any edits** — that the following are still present:
  - `using Anela.Heblo.Domain.Features.Catalog;` at the top
  - `private readonly ICatalogRepository _catalogRepository;` field
  - `ICatalogRepository catalogRepository` constructor parameter and the `_catalogRepository = catalogRepository;` assignment
  - The two `_catalogRepository.GetByIdAsync(lineRequest.MaterialId, cancellationToken)` calls inside `Handle` at the original lines 80 and 93 (their result is consumed by `UpdateLine`/`AddLine`).

This is a checkpoint, not an edit. If any of the above is missing after Step 2.1–2.3, the edit was too aggressive — revert and redo Step 2.1.

### Step 2.5: Run the new regression test — must now pass

- [ ] **Step 2.5:** Re-run only the new test.

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdatePurchaseOrderHandlerTests"
```

Expected: **PASS**. The mock now records exactly 2 calls to `GetByIdAsync` (the two inside `Handle`), confirming `MapToResponse` no longer hits the catalog.

### Step 2.6: Run the full Purchase test class to confirm no regression

- [ ] **Step 2.6:** Run all Purchase feature tests to confirm sibling handlers (`CreatePurchaseOrderHandlerTests`, `UpdatePurchaseOrderStatusHandlerTests`, etc.) still pass — they should be unaffected.

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Purchase"
```

Expected: **PASS** for all tests in the namespace. If any test fails, stop and investigate — the edit must be behavior-preserving for everything except the catalog call count.

### Step 2.7: Build and format

- [ ] **Step 2.7:** Run the project validation gates required by `CLAUDE.md`.

Run:

```bash
cd backend && dotnet build && dotnet format --verify-no-changes
```

Expected:
- `dotnet build`: **succeeds with zero new warnings**. In particular, no `CS1998` warning on `MapToResponseAsync`/`MapToResponse` (the method is now correctly synchronous).
- `dotnet format --verify-no-changes`: **exit 0**. If it reports formatting differences, run `dotnet format` (without `--verify-no-changes`) and re-run the check.

### Step 2.8: Commit the fix

- [ ] **Step 2.8:** Commit the source edit.

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs
git commit -m "refactor(purchase): remove dead per-line catalog lookup in UpdatePurchaseOrder response mapping"
```

---

## Task 3: Verify no other Purchase handler has the same dead pattern (FR-4)

This is verification work, not editing. It produces the audit line that goes into the PR description.

### Step 3.1: Re-grep all `_catalogRepository.GetByIdAsync` sites under `Features/Purchase/UseCases`

- [ ] **Step 3.1:** Confirm the only remaining call sites are ones with a *used* result.

Run:

```bash
cd backend/src/Anela.Heblo.Application/Features/Purchase/UseCases && \
  grep -rn "_catalogRepository.GetByIdAsync" .
```

Expected output (line numbers shifted after the edit; line 128 in `UpdatePurchaseOrderHandler.cs` must be gone):

```
./GetPurchaseOrderById/GetPurchaseOrderByIdHandler.cs:54:            var catalogItem = await _catalogRepository.GetByIdAsync(materialId, cancellationToken);
./UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs:80:                    var material = await _catalogRepository.GetByIdAsync(lineRequest.MaterialId, cancellationToken);
./UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs:93:                    var material = await _catalogRepository.GetByIdAsync(lineRequest.MaterialId, cancellationToken);
./CreatePurchaseOrder/CreatePurchaseOrderHandler.cs:79:                var material = await _catalogRepository.GetByIdAsync(lineRequest.MaterialId, cancellationToken);
```

Exactly 4 remaining hits. Each one:

- `GetPurchaseOrderByIdHandler.cs:54` — `catalogItem` is consumed to populate `CatalogNote`. **Legitimate, leave it.**
- `UpdatePurchaseOrderHandler.cs:80` — `material` feeds `UpdateLine`. **Used, out of scope.**
- `UpdatePurchaseOrderHandler.cs:93` — `material` feeds `AddLine`. **Used, out of scope.**
- `CreatePurchaseOrderHandler.cs:79` — `material` feeds `AddLine`. **Used, out of scope.**

If the grep shows any **new** call site whose result is not consumed on the next line or two, that is a finding for this brief — repeat Task 2 against that file. Otherwise, the audit is clean.

### Step 3.2: Capture the audit line for the PR description

- [ ] **Step 3.2:** Note the grep result verbatim for inclusion in the PR description under a "Verification" section, e.g.:

> Grep of `_catalogRepository.GetByIdAsync` under `Features/Purchase/UseCases` returns 4 hits: `GetPurchaseOrderByIdHandler.cs:54` (used: `CatalogNote`), `UpdatePurchaseOrderHandler.cs:80`/`:93` (used: `UpdateLine`/`AddLine`), `CreatePurchaseOrderHandler.cs:79` (used: `AddLine`). The dead site at `UpdatePurchaseOrderHandler.cs:128` is gone. The adjacent N+1 in `UpdatePurchaseOrderHandler.Handle` is a separate, follow-up ticket.

No commit in this task — the output goes into the PR description, not the repo.

---

## Task 4: Final validation gates

### Step 4.1: Run the entire backend test suite

- [ ] **Step 4.1:** Confirm nothing outside the Purchase folder breaks.

Run:

```bash
cd backend && dotnet test
```

Expected: **PASS** across the whole solution. If unrelated tests fail, they are pre-existing failures and not the concern of this change — note them and proceed. If a test that mentions `UpdatePurchaseOrder` fails, stop and investigate.

### Step 4.2: Confirm the OpenAPI schema is byte-identical (NFR-3)

- [ ] **Step 4.2:** The `UpdatePurchaseOrderResponse` DTO shape is unchanged (no fields added, removed, or renamed) and the handler still returns the same field values. No frontend regeneration is required. To double-check, confirm no DTO file under `Features/Purchase/Contracts/` was modified:

```bash
git diff --name-only main...HEAD -- backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/
```

Expected: **empty output**. If anything appears, the edit drifted out of scope — revert the DTO change.

### Step 4.3: Self-review against the spec and arch review

- [ ] **Step 4.3:** Walk the spec's acceptance criteria once and confirm each is satisfied by the change set:
  - **FR-1:** The two dead lines (and their comment) are gone from `MapToResponse`. The loop has no awaits. The response's `MaterialName` still comes from `line.MaterialName`. ✅
  - **FR-2 (per arch-review amendment):** `_catalogRepository`, the constructor parameter, and the `using Anela.Heblo.Domain.Features.Catalog;` directive are **retained** because `Handle` still uses them. ✅
  - **FR-3:** Public method signatures of the handler/request/response are unchanged. Validation, persistence, and transaction semantics are untouched. ✅
  - **FR-4:** Grep verified — only one site qualified as dead. ✅
  - **NFR-1:** `PUT /api/purchase-orders/{id}` now issues zero catalog calls during response mapping (proven by Task 1's test). ✅
  - **NFR-2:** `dotnet build` clean, `dotnet format` clean, no `CS1998`. ✅
  - **NFR-3:** HTTP contract and OpenAPI schema unchanged. ✅

If any criterion is unsatisfied, return to the corresponding task. Otherwise, the change is ready to ship.

---

## Notes for the implementer

- **Do not touch `Handle` lines 80 and 93.** They look like the same anti-pattern but their result is consumed. Refactoring them to `GetByIdsAsync` is a separate, explicitly-out-of-scope follow-up.
- **Do not remove `ICatalogRepository` from the constructor.** It is still needed by `Handle`. The spec's conditional FR-2 ("If `MapToResponseAsync` was the only consumer…") is false here — the arch review amended the spec on this point.
- **Do not add `await Task.CompletedTask`** to silence CS1998. The correct fix is converting the method to synchronous (Task 2.2/2.3).
- **Do not change the DTO.** Adding `CatalogNote` (or any other catalog-only field) to `UpdatePurchaseOrderResponse` is explicitly out of scope.
- **Frontend is untouched.** No `npm run build`/lint runs needed for this change.
- **E2E is not required.** Per `CLAUDE.md`, E2E runs nightly; a behavior-preserving dead-code removal does not warrant an on-demand E2E run.
