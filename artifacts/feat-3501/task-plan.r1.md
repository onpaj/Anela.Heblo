# Implementation Plan: Test coverage for `UpdatePurchaseOrderRequestValidator`

## Feature
Add a dedicated FluentValidation unit test suite for `UpdatePurchaseOrderRequestValidator` and its nested `UpdatePurchaseOrderLineRequestValidator`, currently at 0% line coverage, closing the CI coverage-gap flag against the 60% threshold.

## Goal
Create `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs` with two public test classes that exercise every branch of the validator: the `BeAReasonableDate` date-bound predicate (future bound, past bound, null passthrough), the 100-line-item cap plus `NotNull`/`NotEmpty` checks on `Lines`, the `RuleForEach` child-validator wiring, and the `Quantity`/`UnitPrice` numeric boundaries on `UpdatePurchaseOrderLineRequestValidator`. This is a test-only change — no production code (`UpdatePurchaseOrderRequestValidator.cs`, `UpdatePurchaseOrderRequest.cs`) is modified.

## Architecture
Single new file, two public test classes, no new dependencies, no `Validators/` subfolder (this module's own convention — confirmed via the sibling `CreatePurchaseOrderRequestValidatorTests.cs` in the same directory):

```
backend/test/Anela.Heblo.Tests/Features/Purchase/
├── CreatePurchaseOrderRequestValidatorTests.cs      (existing — pattern source, unchanged)
├── UpdatePurchaseOrderHandlerTests.cs                (existing — sibling, same namespace, unchanged)
└── UpdatePurchaseOrderRequestValidatorTests.cs        (NEW — this plan)
```

Namespace: `Anela.Heblo.Tests.Features.Purchase`.

- **Class 1 — `UpdatePurchaseOrderRequestValidatorTests`**: exercises `UpdatePurchaseOrderRequestValidator` (parent). Covers `ExpectedDeliveryDate`'s `BeAReasonableDate` (both bounds + null passthrough), `Lines`' `NotNull`/`NotEmpty`/100-cap rules, and one wiring-confirmation test proving `RuleForEach(x => x.Lines).SetValidator(...)` delegates into the child validator.
- **Class 2 — `UpdatePurchaseOrderLineRequestValidatorTests`**: exercises `UpdatePurchaseOrderLineRequestValidator` standalone, directly against a bare `UpdatePurchaseOrderLineRequest`, mirroring the sibling `CreatePurchaseOrderLineRequestValidatorTests`. Primary mechanism for the `Quantity`/`UnitPrice` boundary FRs.

## Tech Stack
- xUnit (`[Fact]`), already referenced by `Anela.Heblo.Tests.csproj`.
- `FluentValidation.TestHelper` (`TestValidate`, `ShouldHaveValidationErrorFor`, `ShouldNotHaveValidationErrorFor`, `ShouldNotHaveAnyValidationErrors`), available transitively via the `FluentValidation` package (v11.9.0) already used by `CreatePurchaseOrderRequestValidatorTests.cs` in the same directory — no `.csproj` changes needed.
- No mocks, no DI container, no database — pure in-memory unit tests.

## Validator source of truth (read in full; reproduced here for reference — do not edit)

`backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs`:

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;

public class UpdatePurchaseOrderRequestValidator : AbstractValidator<UpdatePurchaseOrderRequest>
{
    public UpdatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).WithMessage("Invalid purchase order ID");

        RuleFor(x => x.SupplierId)
            .GreaterThan(0).WithMessage("Supplier is required");

        RuleFor(x => x.ExpectedDeliveryDate)
            .Must(BeAReasonableDate).When(x => x.ExpectedDeliveryDate.HasValue)
            .WithMessage("Expected delivery date must be reasonable (not more than 2 years in the future)");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters");

        RuleFor(x => x.OrderNumber)
            .MaximumLength(50).WithMessage("Order number cannot exceed 50 characters");

        RuleFor(x => x.Lines)
            .NotNull().WithMessage("Order lines are required")
            .NotEmpty().WithMessage("At least one order line is required")
            .Must(lines => lines.Count <= 100).WithMessage("A purchase order cannot have more than 100 line items");

        RuleForEach(x => x.Lines)
            .SetValidator(new UpdatePurchaseOrderLineRequestValidator());
    }

    private bool BeAReasonableDate(DateTime? date)
    {
        if (!date.HasValue)
            return true;

        var maxFutureDate = DateTime.UtcNow.AddYears(2);
        var minPastDate = DateTime.UtcNow.AddYears(-10);

        return date.Value >= minPastDate && date.Value <= maxFutureDate;
    }
}

public class UpdatePurchaseOrderLineRequestValidator : AbstractValidator<UpdatePurchaseOrderLineRequest>
{
    public UpdatePurchaseOrderLineRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).When(x => x.Id.HasValue)
            .WithMessage("Invalid line ID");

        RuleFor(x => x.MaterialId)
            .NotEmpty().WithMessage("Material ID is required")
            .MaximumLength(50).WithMessage("Material ID cannot exceed 50 characters");

        RuleFor(x => x.Name)
            .MaximumLength(200).WithMessage("Name cannot exceed 200 characters");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than 0")
            .LessThanOrEqualTo(999999.99m).WithMessage("Quantity cannot exceed 999999.99");

        RuleFor(x => x.UnitPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Unit price cannot be negative")
            .LessThanOrEqualTo(999999.99m).WithMessage("Unit price cannot exceed 999999.99");

        RuleFor(x => x.Notes)
            .MaximumLength(500).WithMessage("Notes cannot exceed 500 characters");
    }
}
```

`backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequest.cs` (relevant shape, unchanged):

```csharp
public class UpdatePurchaseOrderRequest : IRequest<UpdatePurchaseOrderResponse>
{
    public int Id { get; set; }
    public long SupplierId { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public ContactVia? ContactVia { get; set; }
    public string? Notes { get; set; }
    public List<UpdatePurchaseOrderLineRequest> Lines { get; set; } = null!;
    public string? OrderNumber { get; set; }
}

public class UpdatePurchaseOrderLineRequest
{
    public int? Id { get; set; }
    public string MaterialId { get; set; } = null!;
    public string? Name { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Notes { get; set; }
}
```

## Important implementation note — a genuine spec/clock-skew discrepancy (read before Task 1)

FR-3's first acceptance criterion (spec.r1.md) asks for a test asserting that `ExpectedDeliveryDate = DateTime.UtcNow.AddYears(-10)`, computed in the test **with no offset**, passes validation (`ShouldNotHaveValidationErrorFor`). This is **not achievable as literally written** and would fail on virtually every run, not just flake occasionally. Reasoning:

- The test reads `DateTime.UtcNow` at time `T1` to build `ExpectedDeliveryDate = T1.AddYears(-10)`.
- The validator's own `BeAReasonableDate` reads `DateTime.UtcNow` at time `T2` to compute `minPastDate = T2.AddYears(-10)`, where `T2` is always `>= T1` (the validator call happens strictly after the test constructs the request).
- Because `T2 >= T1`, `minPastDate = T2 - 10y >= T1 - 10y = date.Value`. The rule requires `date.Value >= minPastDate` to pass — which only holds in the razor-thin case of exact clock equality. In practice `T2 > T1` by at least a few microseconds, so `date.Value < minPastDate` and the validator raises an error, contradicting the spec's "passes validation" expectation.
- The upper (future) bound does **not** have this problem: `maxFutureDate = T2 + 2y >= T1 + 2y = date.Value`, so `date.Value <= maxFutureDate` holds regardless of the `T2 >= T1` skew. Only the lower bound is affected, because subtracting years reverses which side of the inequality the "later read" lands on.

Per spec NFR-3 ("stop and flag" on a genuine discrepancy) — this plan flags it here and resolves it pragmatically instead of shipping a test that fails on every CI run: the past-boundary "exactly at the edge" test uses a 1-second forward buffer (`PastYears(10).AddSeconds(1)`) to land deterministically on the valid side of the boundary, with an inline code comment explaining why. This preserves the intent of FR-3 (exercise the `-10` year edge, assert it validates) without introducing a test that is broken by construction. Task 1 below implements this directly — no further action needed from later tasks.

---

### task: scaffold-and-date-boundary-tests

**Goal:** Create the new test file with class 1's scaffold (validator instance, `ValidRequest()` factory, date helpers) plus every `ExpectedDeliveryDate` test (FR-1 baseline, FR-2 future bound, FR-3 past bound with the clock-skew fix above, FR-4 null passthrough).

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs`

- [ ] **Step 1: Create the test file with the scaffold and date-boundary tests**

Use the Write tool to create `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs` with exactly this content:

```csharp
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class UpdatePurchaseOrderRequestValidatorTests
{
    private readonly UpdatePurchaseOrderRequestValidator _validator = new();

    private static DateTime FutureYears(int years) => DateTime.UtcNow.AddYears(years);
    private static DateTime PastYears(int years) => DateTime.UtcNow.AddYears(-years);

    private static UpdatePurchaseOrderRequest ValidRequest() => new()
    {
        Id = 1,
        SupplierId = 1,
        ExpectedDeliveryDate = null,
        Lines = new List<UpdatePurchaseOrderLineRequest>
        {
            new() { MaterialId = "MAT-001", Quantity = 1m, UnitPrice = 1m }
        }
    };

    // --------------- Baseline ---------------

    [Fact]
    public void ValidRequest_PassesAllValidation()
    {
        var request = ValidRequest();

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveAnyValidationErrors();
    }

    // --------------- ExpectedDeliveryDate: future bound ---------------

    [Fact]
    public void ExpectedDeliveryDate_ExactlyTwoYearsInFuture_PassesValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = FutureYears(2);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }

    [Fact]
    public void ExpectedDeliveryDate_TwoYearsAndOneDayInFuture_FailsValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = FutureYears(2).AddDays(1);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ExpectedDeliveryDate)
            .WithErrorMessage("Expected delivery date must be reasonable (not more than 2 years in the future)");
    }

    [Fact]
    public void ExpectedDeliveryDate_OneDayInsideFutureBound_PassesValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = FutureYears(2).AddDays(-1);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }

    // --------------- ExpectedDeliveryDate: past bound ---------------

    [Fact]
    public void ExpectedDeliveryDate_AtTenYearPastBoundary_PassesValidation()
    {
        // NOTE: The validator computes its own `DateTime.UtcNow` internally, and that
        // read always happens strictly after the line below executes. A zero-offset
        // "DateTime.UtcNow.AddYears(-10)" value would therefore always be a hair
        // *earlier* than the validator's own lower bound (which is anchored to a
        // later "now") and would incorrectly fail on every run. A tiny forward buffer
        // neutralizes that read-order skew while still targeting the -10 year edge.
        var request = ValidRequest();
        request.ExpectedDeliveryDate = PastYears(10).AddSeconds(1);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }

    [Fact]
    public void ExpectedDeliveryDate_TenYearsAndOneDayInPast_FailsValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = PastYears(10).AddDays(-1);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ExpectedDeliveryDate)
            .WithErrorMessage("Expected delivery date must be reasonable (not more than 2 years in the future)");
    }

    [Fact]
    public void ExpectedDeliveryDate_OneDayInsidePastBound_PassesValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = PastYears(10).AddDays(1);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }

    // --------------- ExpectedDeliveryDate: null passthrough ---------------

    [Fact]
    public void ExpectedDeliveryDate_Null_PassesValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = null;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }
}
```

- [ ] **Step 2: Build the test project**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Run the new tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderRequestValidatorTests"
```
Expected: all 8 tests pass (`ValidRequest_PassesAllValidation`, `ExpectedDeliveryDate_ExactlyTwoYearsInFuture_PassesValidation`, `ExpectedDeliveryDate_TwoYearsAndOneDayInFuture_FailsValidation`, `ExpectedDeliveryDate_OneDayInsideFutureBound_PassesValidation`, `ExpectedDeliveryDate_AtTenYearPastBoundary_PassesValidation`, `ExpectedDeliveryDate_TenYearsAndOneDayInPast_FailsValidation`, `ExpectedDeliveryDate_OneDayInsidePastBound_PassesValidation`, `ExpectedDeliveryDate_Null_PassesValidation`), 0 failed.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs
git commit -m "Add ExpectedDeliveryDate boundary tests for UpdatePurchaseOrderRequestValidator"
```

---

### task: lines-collection-and-wiring-tests

**Goal:** Extend `UpdatePurchaseOrderRequestValidatorTests` (class 1, created by the `scaffold-and-date-boundary-tests` task) with the `Lines` collection tests — `NotNull`/`NotEmpty` (FR-6), the 100-item cap (FR-5) — plus one `RuleForEach` child-validator wiring-confirmation test (part of FR-7, parent-level only; the full `Quantity`/`UnitPrice` boundary coverage is added by the `line-item-standalone-validator-tests` task).

**Precondition:** `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs` already exists with the `UpdatePurchaseOrderRequestValidatorTests` class ending in an `ExpectedDeliveryDate_Null_PassesValidation` test method immediately followed by the class's closing brace (produced by the prior task). If the file does not yet exist or does not match, create/restore it first using the exact content shown in the `scaffold-and-date-boundary-tests` task's Step 1 before proceeding.

**Files:**
- Edit: `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs`

- [ ] **Step 1: Read the file**

Read `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs` to confirm its current end-of-class content matches the precondition above.

- [ ] **Step 2: Insert the Lines tests before the class's closing brace**

Using the Edit tool, replace this exact text:

```csharp
    [Fact]
    public void ExpectedDeliveryDate_Null_PassesValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = null;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }
}
```

with:

```csharp
    [Fact]
    public void ExpectedDeliveryDate_Null_PassesValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = null;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }

    // --------------- Lines: null / empty / cap ---------------

    [Fact]
    public void Lines_Null_FailsValidation()
    {
        var request = ValidRequest();
        request.Lines = null!;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Lines)
            .WithErrorMessage("Order lines are required");
    }

    [Fact]
    public void Lines_Empty_FailsValidation()
    {
        var request = ValidRequest();
        request.Lines = new List<UpdatePurchaseOrderLineRequest>();

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Lines)
            .WithErrorMessage("At least one order line is required");
    }

    [Fact]
    public void Lines_Exactly100Items_PassesValidation()
    {
        var request = ValidRequest();
        request.Lines = Enumerable.Range(1, 100)
            .Select(i => new UpdatePurchaseOrderLineRequest
            {
                MaterialId = $"MAT-{i}",
                Quantity = 1m,
                UnitPrice = 1m
            })
            .ToList();

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.Lines);
    }

    [Fact]
    public void Lines_101Items_FailsValidation()
    {
        var request = ValidRequest();
        request.Lines = Enumerable.Range(1, 101)
            .Select(i => new UpdatePurchaseOrderLineRequest
            {
                MaterialId = $"MAT-{i}",
                Quantity = 1m,
                UnitPrice = 1m
            })
            .ToList();

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Lines)
            .WithErrorMessage("A purchase order cannot have more than 100 line items");
    }

    // --------------- Lines[0]: RuleForEach wiring confirmation ---------------

    [Fact]
    public void Lines_ChildValidatorWiring_InvalidLineQuantity_FailsValidationOnParent()
    {
        var request = ValidRequest();
        request.Lines[0].Quantity = 0m;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor("Lines[0].Quantity")
            .WithErrorMessage("Quantity must be greater than 0");
    }
}
```

- [ ] **Step 3: Build the test project**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Run the new tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderRequestValidatorTests"
```
Expected: all 13 tests in `UpdatePurchaseOrderRequestValidatorTests` pass (the 8 from the previous task plus `Lines_Null_FailsValidation`, `Lines_Empty_FailsValidation`, `Lines_Exactly100Items_PassesValidation`, `Lines_101Items_FailsValidation`, `Lines_ChildValidatorWiring_InvalidLineQuantity_FailsValidationOnParent`), 0 failed.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs
git commit -m "Add Lines collection and RuleForEach wiring tests for UpdatePurchaseOrderRequestValidator"
```

---

### task: line-item-standalone-validator-tests

**Goal:** Add the second public test class, `UpdatePurchaseOrderLineRequestValidatorTests`, to the same file, testing `UpdatePurchaseOrderLineRequestValidator` standalone against a bare `UpdatePurchaseOrderLineRequest` — full `Quantity` (FR-7) and `UnitPrice` (FR-8) boundary coverage, plus a full-valid-line smoke test.

**Precondition:** `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs` already exists and ends with the `Lines_ChildValidatorWiring_InvalidLineQuantity_FailsValidationOnParent` test method immediately followed by the closing brace of `UpdatePurchaseOrderRequestValidatorTests` (produced by the `lines-collection-and-wiring-tests` task), and that closing brace is the last line in the file. If the file does not match this shape, first apply the `scaffold-and-date-boundary-tests` and `lines-collection-and-wiring-tests` tasks' steps in order before proceeding.

**Files:**
- Edit: `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs`

- [ ] **Step 1: Read the file**

Read `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs` to confirm the current end-of-file content matches the precondition above.

- [ ] **Step 2: Append the second test class**

Using the Edit tool, replace this exact text (the last method and closing brace of class 1):

```csharp
    [Fact]
    public void Lines_ChildValidatorWiring_InvalidLineQuantity_FailsValidationOnParent()
    {
        var request = ValidRequest();
        request.Lines[0].Quantity = 0m;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor("Lines[0].Quantity")
            .WithErrorMessage("Quantity must be greater than 0");
    }
}
```

with:

```csharp
    [Fact]
    public void Lines_ChildValidatorWiring_InvalidLineQuantity_FailsValidationOnParent()
    {
        var request = ValidRequest();
        request.Lines[0].Quantity = 0m;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor("Lines[0].Quantity")
            .WithErrorMessage("Quantity must be greater than 0");
    }
}

public class UpdatePurchaseOrderLineRequestValidatorTests
{
    private readonly UpdatePurchaseOrderLineRequestValidator _validator = new();

    private static UpdatePurchaseOrderLineRequest ValidLine() => new()
    {
        MaterialId = "MAT-001",
        Quantity = 1m,
        UnitPrice = 1m
    };

    // --------------- Quantity ---------------

    [Fact]
    public void Quantity_Zero_FailsValidation()
    {
        var line = ValidLine();
        line.Quantity = 0m;

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.Quantity)
            .WithErrorMessage("Quantity must be greater than 0");
    }

    [Fact]
    public void Quantity_Negative_FailsValidation()
    {
        var line = ValidLine();
        line.Quantity = -1m;

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.Quantity)
            .WithErrorMessage("Quantity must be greater than 0");
    }

    [Fact]
    public void Quantity_SmallestValidIncrement_PassesValidation()
    {
        var line = ValidLine();
        line.Quantity = 0.01m;

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveValidationErrorFor(x => x.Quantity);
    }

    [Fact]
    public void Quantity_AtMaximum_PassesValidation()
    {
        var line = ValidLine();
        line.Quantity = 999999.99m;

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveValidationErrorFor(x => x.Quantity);
    }

    [Fact]
    public void Quantity_ExceedsMaximum_FailsValidation()
    {
        var line = ValidLine();
        line.Quantity = 1000000.00m;

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.Quantity)
            .WithErrorMessage("Quantity cannot exceed 999999.99");
    }

    // --------------- UnitPrice ---------------

    [Fact]
    public void UnitPrice_Zero_PassesValidation()
    {
        var line = ValidLine();
        line.UnitPrice = 0m;

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveValidationErrorFor(x => x.UnitPrice);
    }

    [Fact]
    public void UnitPrice_Negative_FailsValidation()
    {
        var line = ValidLine();
        line.UnitPrice = -0.01m;

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.UnitPrice)
            .WithErrorMessage("Unit price cannot be negative");
    }

    [Fact]
    public void UnitPrice_AtMaximum_PassesValidation()
    {
        var line = ValidLine();
        line.UnitPrice = 999999.99m;

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveValidationErrorFor(x => x.UnitPrice);
    }

    [Fact]
    public void UnitPrice_ExceedsMaximum_FailsValidation()
    {
        var line = ValidLine();
        line.UnitPrice = 1000000.00m;

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.UnitPrice)
            .WithErrorMessage("Unit price cannot exceed 999999.99");
    }

    // --------------- Full valid line ---------------

    [Fact]
    public void ValidLine_PassesAllValidation()
    {
        var line = ValidLine();

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
```

- [ ] **Step 3: Build the test project**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Run the full new file's test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderRequestValidatorTests|FullyQualifiedName~UpdatePurchaseOrderLineRequestValidatorTests"
```
Expected: all 23 tests pass — 13 in `UpdatePurchaseOrderRequestValidatorTests` (from the previous two tasks) plus 10 in `UpdatePurchaseOrderLineRequestValidatorTests` (`Quantity_Zero_FailsValidation`, `Quantity_Negative_FailsValidation`, `Quantity_SmallestValidIncrement_PassesValidation`, `Quantity_AtMaximum_PassesValidation`, `Quantity_ExceedsMaximum_FailsValidation`, `UnitPrice_Zero_PassesValidation`, `UnitPrice_Negative_FailsValidation`, `UnitPrice_AtMaximum_PassesValidation`, `UnitPrice_ExceedsMaximum_FailsValidation`, `ValidLine_PassesAllValidation`), 0 failed.

- [ ] **Step 5: Run the full backend test suite to confirm no regressions**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: all tests pass (no pre-existing failures introduced by this change).

- [ ] **Step 6: Format check**

```bash
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs
```
If this reports formatting issues, run without `--verify-no-changes` to apply fixes, then re-run Step 5.

- [ ] **Step 7: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs
git commit -m "Add standalone Quantity/UnitPrice boundary tests for UpdatePurchaseOrderLineRequestValidator"
```
