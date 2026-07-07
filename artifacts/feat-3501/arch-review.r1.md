# Architecture Review: Test coverage for `UpdatePurchaseOrderRequestValidator`

## Skip Design: true

## Architectural Fit Assessment

This is a pure test-authoring task with zero production-code impact and zero UI surface. It fits cleanly into the existing xUnit + FluentValidation.TestHelper convention already used throughout `backend/test/Anela.Heblo.Tests/`. No new architectural concepts are introduced — the work is to close a coverage gap on a validator class that already exists and is already wired into the MediatR pipeline.

Critically, the spec anchors its pattern on `UpdateProductCompositionOrderRequestValidatorTests.cs` (Catalog module), but a **closer, same-module, structurally near-identical precedent already exists**: `backend/test/Anela.Heblo.Tests/Features/Purchase/CreatePurchaseOrderRequestValidatorTests.cs`. I read it in full. It validates `CreatePurchaseOrderRequestValidator` / `CreatePurchaseOrderLineRequestValidator`, which has the **exact same shape** as the validator under test here:
- Same 100-line-item cap with the same error message (`"A purchase order cannot have more than 100 line items"`).
- Same line-level `Quantity`/`UnitPrice` bounds with the **same error messages** (`"Quantity must be greater than 0"`, `"Quantity cannot exceed 999999.99"`, `"Unit price cannot be negative"`, `"Unit price cannot exceed 999999.99"`).
- Same flat directory placement: `Anela.Heblo.Tests.Features.Purchase` namespace, no `Validators` subfolder.
- Same file organization: **two public test classes in one file** — `CreatePurchaseOrderRequestValidatorTests` (parent) and `CreatePurchaseOrderLineRequestValidatorTests` (line validator tested **standalone**, instantiating `new CreatePurchaseOrderLineRequestValidator()` directly against a bare line DTO), not via `RuleForEach` string-path assertions against the parent.

This sibling file is a stronger reference than the Catalog one the spec cites, because it is the same module, same author intent, and literally shares constants and messages with the validator under test. The architecture guidance below adjusts the spec's FR-7/FR-8 approach accordingly (see Decision 2).

## Proposed Architecture

### Component Overview

```
backend/test/Anela.Heblo.Tests/Features/Purchase/
├── CreatePurchaseOrderRequestValidatorTests.cs      (existing — pattern source)
├── UpdatePurchaseOrderHandlerTests.cs                (existing — sibling, same namespace)
└── UpdatePurchaseOrderRequestValidatorTests.cs        (NEW — this ticket)
```

No new components, no new dependencies, no changes to `Anela.Heblo.Tests.csproj` (FluentValidation.TestHelper and Xunit are already referenced and used by `CreatePurchaseOrderRequestValidatorTests.cs` in the same directory).

### Key Design Decisions

#### Decision 1: File location and namespace — flat `Features/Purchase/`, no `Validators` subfolder
**Options considered:**
- Mirror Catalog's convention: `Features/Purchase/Validators/UpdatePurchaseOrderRequestValidatorTests.cs`.
- Mirror the Purchase module's own convention: flat `Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs`.

**Chosen approach:** Flat, no subfolder — `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs`, namespace `Anela.Heblo.Tests.Features.Purchase`.

**Rationale:** The Purchase module already has a validator test at this flat location (`CreatePurchaseOrderRequestValidatorTests.cs`) sitting alongside handler tests (`UpdatePurchaseOrderHandlerTests.cs`, `CreatePurchaseOrderHandlerTests.cs`) with no subfolder. Module-local precedent overrides a cross-module convention (Catalog/Analytics/UserManagement use a `Validators/` subfolder, but Purchase does not). Confirmed empirically via directory listing — this matches the spec's own conclusion (FR-1), and I've verified it against the actual sibling file, not just its stated intent.

#### Decision 2: Line-validator testing strategy — standalone test class, not string-path assertions
**Options considered:**
- (Spec's primary suggestion, FR-7) Test `Quantity`/`UnitPrice` bounds only through the parent validator via `ShouldHaveValidationErrorFor("Lines[0].Quantity")` string-path syntax, mirroring Catalog's `Order[0].X` pattern.
- (Purchase module's actual precedent) A **second, standalone public test class** `UpdatePurchaseOrderLineRequestValidatorTests` in the same file, instantiating `new UpdatePurchaseOrderLineRequestValidator()` directly against a bare `UpdatePurchaseOrderLineRequest`, exactly as `CreatePurchaseOrderLineRequestValidatorTests` does.

**Chosen approach:** Use the standalone-class pattern as the primary mechanism for `Quantity`/`UnitPrice`/`MaterialId` bounds (FR-7, FR-8), plus **one** integration-style test on the parent validator (`Lines[0].Quantity` string-path, one `[Fact]`) to confirm `RuleForEach(...).SetValidator(...)` is correctly wired — i.e. that the parent actually cascades into the child validator, which the standalone class alone cannot prove.

**Rationale:** The spec explicitly listed the string-path route as primary and the standalone route as merely "optional, at the test author's discretion" — but the standalone-class approach is the pattern this exact module already uses for the structurally-identical sibling validator (`CreatePurchaseOrderLineRequestValidatorTests`), it produces simpler, more readable `[Fact]` bodies (`_validator.TestValidate(line)` vs. building a full parent request just to reach one line field), and it still gets full branch coverage on every `RuleFor` in `UpdatePurchaseOrderLineRequestValidator`. The one added parent-level wiring test closes the only gap the standalone approach leaves (proving `RuleForEach` delegation actually fires), which the Catalog-style spec draft achieves implicitly but at the cost of clunkier test bodies. This is a specification amendment, not a rewrite — see below.

## Implementation Guidance

### Directory / Module Structure

Single new file:
`backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs`

Contains two public classes (mirroring `CreatePurchaseOrderRequestValidatorTests.cs` exactly):
1. `UpdatePurchaseOrderRequestValidatorTests` — parent validator, covers FR-1 through FR-6, plus one `Lines[0].Quantity` wiring-confirmation test.
2. `UpdatePurchaseOrderLineRequestValidatorTests` — line validator, covers FR-7 and FR-8 as standalone `TestValidate(line)` calls.

### Interfaces and Contracts

No production interfaces change. Test-facing "contract" is the FluentValidation `TestValidate` API already in use project-wide:
```csharp
private readonly UpdatePurchaseOrderRequestValidator _validator = new();

result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
result.ShouldHaveValidationErrorFor(x => x.Lines)
    .WithErrorMessage("A purchase order cannot have more than 100 line items");
```
Follow the existing field-initializer style (`private readonly X _validator = new();`) used in `CreatePurchaseOrderRequestValidatorTests.cs` rather than a constructor body, for consistency within the same directory (note: `UpdateProductCompositionOrderRequestValidatorTests.cs` uses a constructor — both styles coexist in the codebase; prefer matching the same-module sibling here).

Date-boundary helpers must be computed at test-run time from `DateTime.UtcNow`, exactly as the spec's NFR-2 requires and as `CreatePurchaseOrderRequestValidatorTests.cs` already does with its own `FutureStr`/`PastStr` helpers (same technique, applied to `AddYears` instead of `AddDays`):
```csharp
private static DateTime FutureYears(int years) => DateTime.UtcNow.AddYears(years);
private static DateTime PastYears(int years) => DateTime.UtcNow.AddYears(-years);
```

`ValidRequest()` factory (per FR-1), matching the sibling's `ValidRequest()` shape:
```csharp
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
```

### Data Flow

Test-only; no runtime data flow changes. Each test: construct request/line DTO in memory → `_validator.TestValidate(...)` → assert on the `FluentValidation.Results.ValidationResult` wrapper. No mocks, no DB, no MediatR pipeline involvement — matches NFR-1 (no I/O).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Off-by-one direction errors in date-boundary tests (e.g. asserting the wrong side fails) | Low | Follow FR-2/FR-3's three-point pattern per bound (exact boundary passes, one unit past boundary fails, safely-inside-boundary passes) — already how `CreatePurchaseOrderRequestValidatorTests.cs` tests its 30-day future bound |
| Divergence between parent-level (`Lines[0].X`) and standalone line-validator test coverage causing false confidence that `RuleForEach` wiring works | Low | Keep the one parent-level `Lines[0].Quantity` wiring test (Decision 2) in addition to the standalone class — do not drop it even though most assertions move to the standalone class |
| Test flakiness from `DateTime.UtcNow` being called at slightly different instants in test vs. validator | Very Low | Only relevant at exact-boundary values; the ±1-day buffer already built into FR-2/FR-3 (`AddDays(1)`/`AddDays(-1)` safely-inside cases) makes millisecond-level timing irrelevant. Exact-boundary cases (`AddYears(2)` with no day offset) carry a theoretical sub-millisecond race but are not observable in practice for year-granularity bounds |

## Specification Amendments

1. **FR-7 approach reprioritized**: The spec listed string-path parent assertions (`ShouldHaveValidationErrorFor("Lines[0].Quantity")`) as the primary mechanism and a standalone `UpdatePurchaseOrderLineRequestValidator` test class as merely "optional, at the test author's discretion." Flip this: make the standalone `UpdatePurchaseOrderLineRequestValidatorTests` class (testing `Quantity`, `UnitPrice`, and incidentally `MaterialId`) the primary mechanism for FR-7/FR-8, and keep exactly one parent-level `Lines[0].Quantity` string-path test to confirm `RuleForEach(...).SetValidator(...)` wiring — mirroring `CreatePurchaseOrderRequestValidatorTests.cs` + `CreatePurchaseOrderLineRequestValidatorTests`, the module's actual existing precedent for this exact validator shape.
2. **No other functional changes.** FR-1 through FR-6, FR-8, and all NFRs are confirmed correct against the real validator source (`UpdatePurchaseOrderRequestValidator.cs` read in full) and require no adjustment — all quoted error messages, boundary values (`AddYears(2)`, `AddYears(-10)`, 100-line cap, `999999.99m` decimal bounds) match the source exactly.

## Prerequisites

None. The test project, FluentValidation.TestHelper reference, and Xunit runner are already in place and already exercise this exact pattern in the same directory (`CreatePurchaseOrderRequestValidatorTests.cs`). Implementation can start immediately — no scaffolding, config, or infrastructure work needed.
