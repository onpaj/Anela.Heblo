# Architecture Review: Fix misleading TargetAmount validation message and add test coverage for SubmitStockTakingRequestValidator

## Skip Design: true

This is a backend-only, single-string message correction plus new unit tests. No new or changed UI components, screens, or visual behavior are involved — the message text is surfaced through existing FluentValidation error plumbing, not redesigned.

## Architectural Fit Assessment

This fits the codebase's established Vertical Slice pattern cleanly, with no new architectural surface:

- `SubmitStockTakingRequestValidator` already lives at `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingRequestValidator.cs`, colocated with its `SubmitStockTakingRequest`/handler, matching the convention used throughout `Features/Catalog/UseCases/*` (e.g. `UpdateProductCompositionOrder`, `RecalculateProductWeight`, inventory use cases).
- FluentValidation is the established validation mechanism for MediatR requests in this codebase (confirmed via `docs/architecture/testing-strategy.md`, which explicitly lists "Validators: FluentValidation rules and edge cases" as a required unit-test target).
- No handler, controller, DTO, or contract changes are needed. The only production change is a string literal inside an existing `WithMessage(...)` call — everything else is additive test coverage.

One structural point to flag, not fix: `SubmitStockTakingRequest.TargetAmount` also carries a `[Range(0, 999999.99, ...)]` `System.ComponentModel.DataAnnotations` attribute with its own message ("Target amount must be between 0 and 999999.99"), independent of the FluentValidation `LessThan(100000)` rule. These two validation layers already disagree on the effective upper bound (999999.99 vs 100000) and MediatR pipeline behavior determines which one actually fires first for a given host (Data Annotations only run if something evaluates them — e.g. model binding in a Web API action — while FluentValidation runs via the MediatR pipeline behavior). This is a pre-existing inconsistency, not introduced by this change, and the spec explicitly excludes touching `TargetAmount` rules beyond the message text. Do not resolve it as part of this task — call it out as a separate follow-up (see Specification Amendments).

## Proposed Architecture

### Component Overview

```
SubmitStockTakingRequest (DTO, unchanged)
        │
        ▼
SubmitStockTakingRequestValidator (FluentValidation, MODIFIED: 1 message string)
        │  ProductCode: NotEmpty, MaximumLength(50)
        │  TargetAmount: GreaterThanOrEqualTo(0), LessThan(100000)
        ▼
[MediatR validation pipeline behavior] → SubmitStockTakingHandler (untouched)

SubmitStockTakingRequestValidatorTests (NEW)
        │  exercises validator directly via FluentValidation.TestHelper.TestValidate()
        ▼
backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/
```

No new components, no new interfaces, no data flow changes. The only edge added to the graph is the new test file's dependency on the validator class (a testing-only edge, not a production one).

### Key Design Decisions

#### Decision 1: Message-only fix, no rule change
**Options considered:**
1. Correct the message to "100,000" (matches spec/brief, matches confirmed intent).
2. Tighten the rule to `LessThan(1000)` to match the existing (wrong) message.

**Chosen approach:** Option 1 — correct the message string only; the rule `LessThan(100000)` stays as-is.

**Rationale:** The brief and spec both state the domain owner confirmed the rule is correct and real users rely on the 100,000 ceiling; tightening it would be a breaking behavior change disguised as a "fix." This is purely a string correction: `"Target amount must be less than 1,000"` → `"Target amount must be less than 100,000"`.

#### Decision 2: Test file location and structure
**Options considered:**
1. Place tests under `Features/Catalog/UseCases/SubmitStockTaking/` to mirror the source tree exactly.
2. Place tests under `Features/Catalog/Validators/`, matching the existing convention for other Catalog validators whose source lives inside `UseCases/*` subfolders.

**Chosen approach:** Option 2.

**Rationale:** Verified against the existing test tree — `GetCatalogDetailRequestValidatorTests.cs` and `UpdateProductCompositionOrderRequestValidatorTests.cs` both test validators whose source lives in `Features/Catalog/UseCases/{UseCase}/` but whose tests live in `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/`. This is the established, repo-wide convention for this module (source is colocated with its use case; validator tests are centralized under a `Validators` folder per feature module). New test file: `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/SubmitStockTakingRequestValidatorTests.cs`.

#### Decision 3: Test style — `TestValidate()` over manual assertion
**Options considered:**
1. Manually construct `ValidationContext`/`Validate()` and inspect `Errors`.
2. Use `FluentValidation.TestHelper`'s `TestValidate()` + `ShouldHaveValidationErrorFor`/`ShouldNotHaveValidationErrorFor`/`WithErrorMessage`/`ShouldNotHaveAnyValidationErrors`.

**Chosen approach:** Option 2, matching `GetCatalogDetailRequestValidatorTests.cs` exactly (same package already referenced: `FluentValidation.TestHelper`, `FluentAssertions`, `Xunit`).

**Rationale:** This is the uniform pattern across every validator test file found in the repo (`GetCatalogDetailRequestValidatorTests`, `UpdateProductCompositionOrderRequestValidatorTests`, `GetConsumptionHistoryRequestValidatorTests`, etc.). No reason to deviate; consistency lowers review friction for a solo-maintained codebase.

## Implementation Guidance

### Directory / Module Structure

- **Modify:** `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingRequestValidator.cs` — change line 19's `WithMessage` string only.
- **Create:** `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/SubmitStockTakingRequestValidatorTests.cs`.
- No other files need to change. No new namespaces, no DI registration changes (the validator is presumably already auto-registered via assembly scanning, as with every other FluentValidation validator in this codebase — confirm registration is unaffected since the class itself is unchanged structurally).

### Interfaces and Contracts

No interface or contract changes. For reference, the class under test:

```csharp
public class SubmitStockTakingRequestValidator : AbstractValidator<SubmitStockTakingRequest>
{
    public SubmitStockTakingRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty().WithMessage("Product code is required")
            .MaximumLength(50).WithMessage("Product code cannot exceed 50 characters");

        RuleFor(x => x.TargetAmount)
            .GreaterThanOrEqualTo(0).WithMessage("Target amount must be greater than or equal to 0")
            .LessThan(100000).WithMessage("Target amount must be less than 100,000"); // ← only this string changes
    }
}
```

`SubmitStockTakingRequest` (`ProductCode: string`, `TargetAmount: decimal`, `SoftStockTaking: bool`) is unchanged.

### Data Flow

Test-only data flow, following the `GetCatalogDetailRequestValidatorTests` template:

1. Test constructs `new SubmitStockTakingRequestValidator()` in the test class constructor (no mocks needed — validator has no dependencies).
2. Each test builds a `SubmitStockTakingRequest` with the property under test set to a boundary/representative value.
3. `_validator.TestValidate(request)` runs the rules synchronously.
4. Assert via `ShouldHaveValidationErrorFor` / `ShouldNotHaveValidationErrorFor` / `.WithErrorMessage(...)` / `ShouldNotHaveAnyValidationErrors()`.

Cover, per spec FR-1 through FR-5 (map directly to `[Theory]`/`[Fact]` methods, mirroring `GetCatalogDetailRequestValidatorTests` naming):
- `TargetAmount_Below100000_PassesValidation` (`[InlineData(500)]`, `[InlineData(0)]`, `[InlineData(1)]`, `[InlineData(99999)]`)
- `TargetAmount_AtOrAbove100000_FailsValidation` with `.WithErrorMessage("Target amount must be less than 100,000")` for `100000` and `100001`
- `TargetAmount_Negative_HasCorrectErrorMessage` (`-1` → `"Target amount must be greater than or equal to 0"`)
- `ProductCode_NullOrEmpty_HasCorrectErrorMessage` (`null`, `""` → `"Product code is required"`)
- `ProductCode_TooLong_HasCorrectErrorMessage` (51 chars → `"Product code cannot exceed 50 characters"`; 50 chars passes)
- `ValidRequest_PassesAllValidation` (`ProductCode = "ABC123"`, `TargetAmount = 500` → `ShouldNotHaveAnyValidationErrors()`)

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Confusing the message fix with a rule change (tightening to 1,000) | Medium | Spec explicitly locks `LessThan(100000)` as unchanged; add a test asserting `TargetAmount = 500` passes, which fails loudly if the rule is ever tightened by mistake |
| Existing `[Range(0, 999999.99)]` Data Annotation on `TargetAmount` disagrees with the FluentValidation `LessThan(100000)` rule | Low (pre-existing, out of scope) | Do not touch in this change; flag as a separate coverage-gap/tech-debt item for a future spec — see Specification Amendments |
| Test file misplaced relative to repo convention, causing review friction or duplicate test discovery confusion | Low | Follow the verified convention: place test in `Features/Catalog/Validators/`, not colocated with the source `UseCases/SubmitStockTaking/` folder |

## Specification Amendments

1. **Test file path** — the spec says "a new or existing unit test file (e.g. `SubmitStockTakingRequestValidatorTests.cs`) under the corresponding test project, following existing conventions" but doesn't name the directory. Confirmed via repo inspection: it must be `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/SubmitStockTakingRequestValidatorTests.cs` (not colocated with source), matching `GetCatalogDetailRequestValidatorTests.cs` and `UpdateProductCompositionOrderRequestValidatorTests.cs`.
2. **Out-of-scope note for future work** — recommend filing a separate follow-up item (not part of this change) to reconcile the `[Range(0, 999999.99)]` Data Annotation on `SubmitStockTakingRequest.TargetAmount` with the FluentValidation `LessThan(100000)` rule; the two disagree on the actual enforced ceiling depending on which validation layer runs. This spec's Out of Scope section already excludes rule changes, so no action is needed now — flagging only so it isn't lost.

## Prerequisites

None. No migrations, no config, no infrastructure changes. `FluentValidation`, `FluentValidation.TestHelper`, `FluentAssertions`, and `Xunit` are already referenced by the existing `Anela.Heblo.Tests` project (confirmed via `GetCatalogDetailRequestValidatorTests.cs`), so no new package references are required.
