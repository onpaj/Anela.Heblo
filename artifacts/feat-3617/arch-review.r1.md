# Architecture Review: Unit Test Coverage for CreateManufactureDifficultyRequestValidator

## Skip Design: true

This is a pure backend test-authoring task against an existing, unmodified FluentValidation validator. There are no new or changed UI components, screens, layouts, API contracts, or visual design decisions involved — the spec explicitly rules out any production code changes. No design review is warranted.

## Architectural Fit Assessment

This feature fits cleanly into the existing test architecture with no new patterns required. The codebase already has an established, repeated convention for FluentValidation validator unit tests:

- `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/SubmitStockTakingRequestValidatorTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/GetCatalogDetailRequestValidatorTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/UpdateProductCompositionOrderRequestValidatorTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchPlanRequestValidatorTests.cs` (source of the cross-field `[Fact]`-style date-range test)

I read the validator under test directly:

```csharp
// backend/src/.../Catalog/Validators/CreateManufactureDifficultyRequestValidator.cs
public class CreateManufactureDifficultyRequestValidator : AbstractValidator<CreateManufactureDifficultyRequest>
{
    public CreateManufactureDifficultyRequestValidator()
    {
        RuleFor(x => x.ProductCode).NotEmpty().WithMessage("Product code is required")
            .MaximumLength(50).WithMessage("Product code cannot exceed 50 characters");

        RuleFor(x => x.DifficultyValue).GreaterThanOrEqualTo(0)
            .WithMessage("Difficulty value must be non-negative");

        RuleFor(x => x.ValidFrom).LessThan(x => x.ValidTo)
            .WithMessage("ValidFrom must be earlier than ValidTo")
            .When(x => x.ValidFrom.HasValue && x.ValidTo.HasValue);

        RuleFor(x => x.ValidTo).GreaterThan(x => x.ValidFrom)
            .WithMessage("ValidTo must be later than ValidFrom")
            .When(x => x.ValidFrom.HasValue && x.ValidTo.HasValue);
    }
}
```

This matches the shape of `SubmitStockTakingRequestValidator` (simple `NotEmpty`/`MaximumLength`/range rule) plus a cross-field `.When(...)`-guarded pair matching the pattern already tested in `CalculateBatchPlanRequestValidatorTests.Validate_InvalidDateRange_FailsValidation`. No new test infrastructure, no new packages, and no new project are needed — `FluentValidation` 11.9.0 is already referenced by `Anela.Heblo.Application.csproj` and flows transitively into the test project, and `FluentValidation.TestHelper` is already used (and thus already resolvable) in `GetCatalogDetailRequestValidatorTests.cs` and `UpdateProductCompositionOrderRequestValidatorTests.cs`.

## Proposed Architecture

### Component Overview

No architectural components change. This is a leaf addition to the existing test tree:

```
backend/test/Anela.Heblo.Tests/
└── Features/
    └── Catalog/
        └── Validators/
            ├── GetCatalogDetailRequestValidatorTests.cs        (existing)
            ├── SubmitStockTakingRequestValidatorTests.cs        (existing, primary pattern source)
            ├── UpdateProductCompositionOrderRequestValidatorTests.cs (existing)
            └── CreateManufactureDifficultyRequestValidatorTests.cs   (NEW — this task)
```

The new test class depends only on:
- `Anela.Heblo.Application.Features.Catalog.Validators.CreateManufactureDifficultyRequestValidator` (system under test, unmodified)
- `Anela.Heblo.Application.Features.Catalog.UseCases.CreateManufactureDifficulty.CreateManufactureDifficultyRequest` (DTO, unmodified)
- `FluentValidation.TestHelper` (`TestValidate`, `ShouldHaveValidationErrorFor`, `ShouldNotHaveValidationErrorFor`, `WithErrorMessage`)
- `Xunit` (`[Fact]`, `[Theory]`, `[InlineData]`)

No mocks, no DI container, no database, no HTTP pipeline — this is an in-process, in-memory unit test against a pure validator.

### Key Design Decisions

#### Decision 1: Test class structure — constructor-instantiated validator + `ValidRequest()` helper
**Options considered:**
- (a) Instantiate a fresh `CreateManufactureDifficultyRequestValidator` per test method (inline `new(...)`).
- (b) Instantiate once via constructor into a `readonly` field, and use a private static `ValidRequest()` factory that individual tests mutate.

**Chosen approach:** (b), exactly mirroring `SubmitStockTakingRequestValidatorTests`.

**Rationale:** This is the dominant, unambiguous convention in every sibling validator test file in this codebase (all three files in `Features/Catalog/Validators/` plus `CalculateBatchPlanRequestValidatorTests`). `AbstractValidator` subclasses are stateless and cheap to construct, so per-class-instance construction is safe and consistent with xUnit's per-test-method class instantiation model (xUnit creates a new test class instance per `[Fact]`/`[Theory]` case, so the constructor still runs once per case — no shared-state risk). Deviating would introduce an unexplained inconsistency for a future maintainer scanning the `Validators/` folder.

#### Decision 2: `[Theory]`/`[InlineData]` for boundary/parameterized cases, `[Fact]` for cross-field scenarios
**Options considered:**
- (a) Use `[Theory]` uniformly, including for the date-range cross-field cases (parameterizing over day-offset).
- (b) Use `[Theory]` for single-field boundary cases (`ProductCode` length, `DifficultyValue` sign) and discrete `[Fact]`s for the `ValidFrom`/`ValidTo` cross-field scenarios, matching `CalculateBatchPlanRequestValidatorTests.Validate_InvalidDateRange_FailsValidation`.

**Chosen approach:** (b), per NFR-3 in the spec and the existing `CalculateBatchPlanRequestValidatorTests` precedent.

**Rationale:** Cross-field date scenarios (`<`, `==`, `>`, single-sided-null ×2, both-null) each assert on **two** properties (`ValidFrom` *and* `ValidTo`) with **different** expected messages per field — cramming that into `InlineData` rows would need multiple bool/message parameters per row and reduce readability versus the marginal DRY benefit. Single-field boundary tests (50 vs. 51 chars, -1 vs. 0 vs. 1) are naturally tabular and match the existing `[Theory]` usage for `TargetAmount` in the sibling file.

## Implementation Guidance

### Directory / Module Structure

Create exactly one new file, no other files touched:

```
backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/CreateManufactureDifficultyRequestValidatorTests.cs
```

Namespace: `Anela.Heblo.Tests.Features.Catalog.Validators` (matches all three sibling files — verified directly).

No changes to `Anela.Heblo.Tests.csproj` — `FluentValidation`, `FluentValidation.TestHelper`, and `xunit` are already resolvable in this project (confirmed via existing imports in `GetCatalogDetailRequestValidatorTests.cs` and `UpdateProductCompositionOrderRequestValidatorTests.cs`).

### Interfaces and Contracts

No new interfaces or contracts. The test targets the existing, unmodified public surface:

```csharp
public class CreateManufactureDifficultyRequestValidator : AbstractValidator<CreateManufactureDifficultyRequest>
```

```csharp
public class CreateManufactureDifficultyRequest : IRequest<CreateManufactureDifficultyResponse>
{
    public string ProductCode { get; set; } = null!;
    public int DifficultyValue { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}
```

Required `using`s (mirroring `SubmitStockTakingRequestValidatorTests.cs` exactly):

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.CreateManufactureDifficulty;
using Anela.Heblo.Application.Features.Catalog.Validators;
using FluentValidation.TestHelper;
using Xunit;
```

Note: `GetCatalogDetailRequestValidatorTests.cs` additionally imports `FluentAssertions` and uses `.Should()` for some assertions, while `SubmitStockTakingRequestValidatorTests.cs` uses plain `Assert.True(...)` / `Assert.Empty(...)` for the whole-request happy-path check (FR-5). Since `SubmitStockTakingRequestValidatorTests` is the explicitly-named pattern source for this task (per brief and spec FR-1/FR-5), follow **its** style (`Assert.True(result.IsValid); Assert.Empty(result.Errors);`) rather than pulling in `FluentAssertions` for this one file — avoids an unnecessary new `using` for no added clarity.

### Data Flow

Pure synchronous, in-memory flow, no I/O:

1. Test method builds a `CreateManufactureDifficultyRequest` via `ValidRequest()` and mutates the field(s) under test.
2. `_validator.TestValidate(request)` runs all `RuleFor(...)` chains synchronously (including the `.When(...)`-guarded cross-field rules, which re-evaluate the guard predicate against the *current* request state).
3. `TestValidationResult<T>` is asserted against via `ShouldHaveValidationErrorFor` / `ShouldNotHaveValidationErrorFor` (+ `.WithErrorMessage(...)`), or via `result.IsValid` / `result.Errors` for the whole-request case.

No handler, no controller, no persistence layer, no MediatR pipeline is exercised — consistent with "Validators: FluentValidation rules and edge cases" being explicitly listed as required unit-test scope in `docs/architecture/testing-strategy.md`.

### Test case checklist (traced to spec FR-1..FR-5, one-to-one, no additions/omissions)

| # | Scenario | Style |
|---|----------|-------|
| 1 | `ProductCode` null/empty → error "Product code is required" | `[Theory]`/`InlineData(null, "")` |
| 2 | `ProductCode` typical value → no error | `[Fact]` |
| 3 | `ProductCode` exactly 50 chars → no error | `[Fact]` |
| 4 | `ProductCode` exactly 51 chars → error "...cannot exceed 50 characters" | `[Fact]` |
| 5 | `DifficultyValue = -1` → error "...must be non-negative" | `[Fact]` |
| 6 | `DifficultyValue = 0` → no error | `[Fact]` |
| 7 | `DifficultyValue = 1` → no error | `[Fact]` (or fold into a `[Theory]` with case 6) |
| 8 | `ValidFrom < ValidTo` → no error on either field | `[Fact]` |
| 9 | `ValidFrom == ValidTo` → error on both fields, respective messages | `[Fact]` |
| 10 | `ValidFrom > ValidTo` → error on both fields, respective messages | `[Fact]` |
| 11 | Only `ValidFrom` set → no error on either field | `[Fact]` |
| 12 | Only `ValidTo` set → no error on either field | `[Fact]` |
| 13 | Neither set (both null) → no error on either field | `[Fact]` |
| 14 | Fully valid request → `result.IsValid == true`, `result.Errors` empty | `[Fact]` (`ValidRequest_PassesAllValidation`) |

This is directly implementable without further clarification — no gaps found between spec and validator source.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `FluentValidation.TestHelper` namespace not resolvable in `Anela.Heblo.Tests.csproj` (no explicit `PackageReference` line for it) | Low | Verified empirically: `GetCatalogDetailRequestValidatorTests.cs` and `UpdateProductCompositionOrderRequestValidatorTests.cs` already `using FluentValidation.TestHelper;` and compile today in this project, so the type is already resolvable transitively via the `FluentValidation` 11.9.0 package reference in `Anela.Heblo.Application.csproj` plus the project reference chain. No csproj change needed; if `dotnet build` somehow disagrees, add `FluentValidation.TestHelper` explicitly rather than guessing further. |
| Developer conflates DTO-level `[Required]`/`[Range]` data-annotation testing with validator testing, adding out-of-scope model-binding tests | Low | Spec explicitly excludes this (see spec "Data Model" note and "Out of Scope"); implementer should test only via `AbstractValidator.TestValidate`, never via `Validator.TryValidateObject` or ASP.NET model binding. |
| Test flakiness from date arithmetic (e.g. using `DateTime.Now` as an anchor) | Low | Use fixed literal `DateTime` values (e.g. `new DateTime(2026, 1, 1)` / `new DateTime(2026, 1, 2)`) for all `ValidFrom`/`ValidTo` cases, not relative-to-`Now` values — avoids any theoretical midnight-rollover flake and matches the deterministic style of `CalculateBatchPlanRequestValidatorTests`. |

## Specification Amendments

None. The specification is complete, correctly scoped, and directly traceable to the validator's actual rules (verified by reading the validator source above — no discrepancy found between spec FR-2/FR-3/FR-4 error messages and the actual `.WithMessage(...)` strings in the validator). No amendments needed.

## Prerequisites

None. No migrations, no config, no infrastructure changes. The target validator, DTO, and test project all already exist and compile. Implementation can start immediately by adding the single new test file described above.
