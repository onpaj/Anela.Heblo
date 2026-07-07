# Design: Test coverage for `UpdatePurchaseOrderRequestValidator`

## Component Design

This is a test-only addition. One new file, two public test classes, no production code touched.

```
backend/test/Anela.Heblo.Tests/Features/Purchase/
├── CreatePurchaseOrderRequestValidatorTests.cs      (existing — pattern source, unchanged)
├── UpdatePurchaseOrderHandlerTests.cs                (existing — sibling, same namespace)
└── UpdatePurchaseOrderRequestValidatorTests.cs        (NEW)
```

Namespace: `Anela.Heblo.Tests.Features.Purchase` (flat, no `Validators` subfolder — matches this module's existing convention, not the Catalog module's).

### Class 1: `UpdatePurchaseOrderRequestValidatorTests`

Responsibility: exercise every rule on `UpdatePurchaseOrderRequestValidator` that operates on the parent request — `ExpectedDeliveryDate`'s `BeAReasonableDate` predicate (both bounds and the null-passthrough branch), and the `Lines` collection's `NotNull`/`NotEmpty`/100-item-cap rules. Also owns exactly one wiring-confirmation test proving `RuleForEach(x => x.Lines).SetValidator(...)` delegates into the child validator.

```csharp
public class UpdatePurchaseOrderRequestValidatorTests
{
    private readonly UpdatePurchaseOrderRequestValidator _validator = new();

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

    private static DateTime FutureYears(int years) => DateTime.UtcNow.AddYears(years);
    private static DateTime PastYears(int years) => DateTime.UtcNow.AddYears(-years);

    // FR-1..FR-6 as [Fact]/[Theory] methods using _validator.TestValidate(request)
    // FR-7 wiring check: exactly one [Fact] asserting on "Lines[0].Quantity" string path
}
```

Covers (mapping to spec FRs):
- FR-1: baseline `ValidRequest()` passes `ShouldNotHaveAnyValidationErrors()`.
- FR-2/FR-3: `ExpectedDeliveryDate` upper/lower `BeAReasonableDate` bounds, three-point pattern per bound (exact boundary → valid, one unit past → invalid with the shared error message, safely inside → valid).
- FR-4: `ExpectedDeliveryDate = null` → no error (covers both the `Must` early-return and the `.When(...)` gate).
- FR-5: `Lines` with exactly 100 items → valid; 101 items → invalid with the cap message; items generated via `Enumerable.Range`.
- FR-6: `Lines = null` and `Lines = []` → their respective messages.
- FR-7 (wiring only): one `Lines[0].Quantity` string-path assertion confirming the child validator is invoked through `RuleForEach`.

### Class 2: `UpdatePurchaseOrderLineRequestValidatorTests`

Responsibility: exercise `UpdatePurchaseOrderLineRequestValidator` standalone, directly against a bare `UpdatePurchaseOrderLineRequest`, mirroring the sibling `CreatePurchaseOrderLineRequestValidatorTests`. This is the primary mechanism for FR-7/FR-8 (per the architecture review's Decision 2 amendment), not the string-path route the spec originally listed as primary.

```csharp
public class UpdatePurchaseOrderLineRequestValidatorTests
{
    private readonly UpdatePurchaseOrderLineRequestValidator _validator = new();

    private static UpdatePurchaseOrderLineRequest ValidLine() => new()
    {
        MaterialId = "MAT-001",
        Quantity = 1m,
        UnitPrice = 1m
    };

    // FR-7, FR-8 as [Theory] methods using _validator.TestValidate(line)
}
```

Covers:
- FR-7: `Quantity` — `0` invalid ("must be greater than 0"), `0.01m` valid, `999999.99m` valid, `1000000.00m` invalid ("cannot exceed 999999.99"), `-1m` invalid ("must be greater than 0").
- FR-8: `UnitPrice` — `0` valid (inclusive lower bound), `-0.01m` invalid ("cannot be negative"), `999999.99m` valid, `1000000.00m` invalid ("cannot exceed 999999.99").

### Test execution contract

Both classes use `FluentValidation.TestHelper`'s `TestValidate(...)` extension against an in-memory DTO instance, asserting via `ShouldHaveValidationErrorFor(...).WithErrorMessage(...)` / `ShouldNotHaveValidationErrorFor(...)` / `ShouldNotHaveAnyValidationErrors()`. No mocks, no DI container, no database, no MediatR pipeline — pure unit tests, consistent with NFR-1 (no I/O) and NFR-2 (date boundaries computed at run time via `DateTime.UtcNow`, never hardcoded).

## Data Schemas

No schema changes. Existing types under test (unchanged), both defined in
`backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequest.cs`:

```csharp
class UpdatePurchaseOrderRequest
{
    int Id;
    long SupplierId;
    DateTime? ExpectedDeliveryDate;
    ContactVia? ContactVia;
    string? Notes;
    List<UpdatePurchaseOrderLineRequest> Lines;
    string? OrderNumber;
}

class UpdatePurchaseOrderLineRequest
{
    int? Id;
    string MaterialId;
    string? Name;
    decimal Quantity;
    decimal UnitPrice;
    string? Notes;
}
```

Relevant FluentValidation rules exercised (source of truth: `UpdatePurchaseOrderRequestValidator.cs`, not reproduced/modified here):

| Field | Rule | Error message |
|---|---|---|
| `ExpectedDeliveryDate` (when non-null) | `Must(BeAReasonableDate)` — valid range `[UtcNow.AddYears(-10), UtcNow.AddYears(2)]` | `"Expected delivery date must be reasonable (not more than 2 years in the future)"` |
| `Lines` | `NotNull()` | `"Order lines are required"` |
| `Lines` | `NotEmpty()` | `"At least one order line is required"` |
| `Lines` | `Must(count <= 100)` | `"A purchase order cannot have more than 100 line items"` |
| `Lines[i]` | `RuleForEach(...).SetValidator(UpdatePurchaseOrderLineRequestValidator)` | — (wiring only) |
| `Quantity` | `GreaterThan(0)` | `"Quantity must be greater than 0"` |
| `Quantity` | `LessThanOrEqualTo(999999.99m)` | `"Quantity cannot exceed 999999.99"` |
| `UnitPrice` | `GreaterThanOrEqualTo(0)` | `"Unit price cannot be negative"` |
| `UnitPrice` | `LessThanOrEqualTo(999999.99m)` | `"Unit price cannot exceed 999999.99"` |

No API request/response shapes, event payloads, or database schemas change as part of this ticket — it is a pure test-authoring task against existing, already-deployed validation rules. The DataAnnotations attributes (`[Required]`, `[Range]`, `[StringLength]`) also present on these DTOs are a separate model-binding validation layer and are explicitly out of scope (not exercised by `AbstractValidator.TestValidate`).
