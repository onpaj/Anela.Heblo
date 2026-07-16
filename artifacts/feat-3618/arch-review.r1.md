# Architecture Review: CalculateBatchByIngredientRequestValidator unit tests

## Skip Design: true

## Architectural Fit Assessment
This is a pure test-addition task with zero production-code footprint. It fits directly into an already-established convention: FluentValidation validators under `backend/src/Anela.Heblo.Application/Features/<Module>/Validators/` are unit-tested 1:1 with `FluentValidation.TestHelper` in `backend/test/Anela.Heblo.Tests/Features/<Module>/`. The target validator, `CalculateBatchByIngredientRequestValidator`, is a trivial three-rule `AbstractValidator<CalculateBatchByIngredientRequest>` (verified by reading the source directly) with no dependencies, no custom validators, and no async rules — there is nothing here that deviates from the pattern already implemented in the sibling file `CalculateBatchPlanRequestValidatorTests.cs` in the same directory, and mirrored again in `GetManufactureOutputRequestValidatorTests.cs` and `GetManufacturingStockAnalysisRequestValidatorTests.cs` in the same `Features/Manufacture` test folder. No new architecture is being introduced; this review exists mainly to confirm there's nothing to design.

Confirmed via source inspection:
- `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` references `xunit`/`xunit.runner.visualstudio` only — no direct `FluentValidation.TestHelper` package entry. This is fine: `FluentValidation.TestHelper` (namespace) ships inside the main `FluentValidation` package (v11.9.0, referenced transitively via `Anela.Heblo.Application.csproj`), which the sibling test already consumes successfully (`using FluentValidation.TestHelper;`). No csproj changes needed.
- The validator's three rules (`ProductCode`: NotEmpty + MaxLength(50); `IngredientCode`: NotEmpty + MaxLength(50); `DesiredIngredientAmount`: GreaterThan(0) + LessThanOrEqualTo(999999.99)) exactly match the brief/spec description — no drift between spec and current code to flag.

## Proposed Architecture

### Component Overview
No new components. One new leaf test file joins an existing flat collection of validator test files:

```
backend/test/Anela.Heblo.Tests/Features/Manufacture/
├── CalculateBatchPlanRequestValidatorTests.cs        (existing, pattern source)
├── CalculateBatchByIngredientHandlerTests.cs         (existing, handler-level, untouched)
└── CalculateBatchByIngredientRequestValidatorTests.cs (NEW — this task)
```

### Key Design Decisions

#### Decision 1: Test style — mirror `CalculateBatchPlanRequestValidatorTests` exactly
**Options considered:** (a) Copy the sibling's structure/conventions verbatim; (b) invent a leaner style (e.g. a single parameterized `[Theory]` covering all fields via reflection or a data-driven table).
**Chosen approach:** (a). One `[Fact]` for the happy path, `[Theory]`/`[InlineData]` per rule/boundary, field-init `_validator` in the constructor, AAA comments (`// Arrange` / `// Act` / `// Assert`).
**Rationale:** This is a solo-maintainer codebase with an established, repeated pattern across multiple validator test files in the same folder. Deviating (option b) would save a handful of lines but break scanability for the next validator test someone copies from. Consistency wins outright for a task this small.

#### Decision 2: One test class, not split by field
**Options considered:** Separate test classes per property (`ProductCode`, `IngredientCode`, `DesiredIngredientAmount`); single class covering all rules of the validator.
**Chosen approach:** Single class `CalculateBatchByIngredientRequestValidatorTests`, matching 1 validator ↔ 1 test class convention used throughout the module.
**Rationale:** The validator itself is a single cohesive unit (3 rules, no sub-validators). Splitting would be over-engineering for ~10-12 test methods total.

## Implementation Guidance

### Directory / Module Structure
Create exactly one file:
`backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchByIngredientRequestValidatorTests.cs`

- Namespace: `Anela.Heblo.Tests.Features.Manufacture`
- Class: `CalculateBatchByIngredientRequestValidatorTests`
- Usings required: `Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient` (for `CalculateBatchByIngredientRequest`), `Anela.Heblo.Application.Features.Manufacture.Validators` (for the validator), `FluentValidation.TestHelper`, `Xunit`.
- No changes to any `.csproj`, no new NuGet packages, no changes to production source.

### Interfaces and Contracts
No new interfaces. Tests exercise the existing public contract only:

```csharp
public class CalculateBatchByIngredientRequest : IRequest<CalculateBatchByIngredientResponse>
{
    public string ProductCode { get; set; } = null!;
    public string IngredientCode { get; set; } = null!;
    public double DesiredIngredientAmount { get; set; }
}
```
and `CalculateBatchByIngredientRequestValidator : AbstractValidator<CalculateBatchByIngredientRequest>` (constructor-only, no injected dependencies — safe to `new` up once per test class instance, matching the sibling's field-init-in-constructor pattern).

Note for the implementer: `ProductCode`/`IngredientCode` are non-nullable (`= null!`) at the C# type level but the spec (FR-5/FR-7) requires a `[InlineData(null)]` case. This is legal and necessary here — `TestValidate` builds the object via property assignment at runtime, and FluentValidation's `NotEmpty()` rule correctly flags a runtime-assigned `null`, exactly as the sibling test already does for `CalculateBatchPlanRequest.ProductCode` (also `null!`-typed). No special handling needed; follow the sibling's precedent verbatim.

### Data Flow
Not applicable in the runtime sense — this is a pure unit test: instantiate `CalculateBatchByIngredientRequest`, call `_validator.TestValidate(request)`, assert on the `TestValidationResult<T>`. No handler, no service, no persistence layer is touched or mocked.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Boundary test for `999999.99` fails due to floating-point comparison inside FluentValidation's `LessThanOrEqualTo` on a `double` | Low | Use the exact literal `999999.99` as specified — FluentValidation's comparison validator uses `CompareTo` on `IComparable`, which is exact for identical literals; the sibling pattern already relies on plain `double` comparisons without issue. No epsilon/tolerance logic needed. |
| Test file name/namespace collision with existing `CalculateBatchByIngredientHandlerTests.cs` in the same folder | Very Low | Confirmed no existing `CalculateBatchByIngredientRequestValidatorTests.cs` file exists yet (directory listing checked) — no collision. |
| Someone later "improves" the validator (e.g. changes the cap or drops `MaximumLength`) without noticing these tests | Low | Out of scope to prevent via architecture — the entire point of this task is that these tests will now catch that regression going forward. |

## Specification Amendments
None. The spec (`spec.r1.md`) is accurate, verified against the actual validator and DTO source, and directly implementable as written — FR-1 through FR-8 map one-to-one onto the validator's three rules with no gaps or conflicts found.

## Prerequisites
None. No migrations, no config, no infrastructure changes. The test project already builds and already has every dependency (`xunit`, transitively-available `FluentValidation.TestHelper`) needed. Implementation can start immediately.
