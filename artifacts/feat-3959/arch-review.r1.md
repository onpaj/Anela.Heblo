# Architecture Review: VatClassificationRule Test Coverage

## Skip Design: true

## Architectural Fit Assessment

This is a pure backend test-authoring task with no production code changes and no UI surface. It fits the existing pattern exactly: `VatClassificationRule` is one of five sibling `IClassificationRule` implementations under `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/`, and four of the five (`AmountClassificationRule`, `CompanyNameClassificationRule`, `DescriptionClassificationRule`, `ItemDescriptionClassificationRule`) already have corresponding xUnit test classes in `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/`. `VatClassificationRuleTests.cs` is a straightforward gap-fill against an established convention, not a new pattern.

I verified the actual source:

```csharp
// backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/VatClassificationRule.cs
public class VatClassificationRule : IClassificationRule
{
    public string Identifier => "ICO";
    public string DisplayName => "IČO";
    public string Description => "Porovnání IČO firmy";

    public bool Evaluate(ReceivedInvoice invoice, string pattern)
    {
        return string.Equals(invoice.CompanyVat?.Trim(), pattern?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
```

`IClassificationRule.Evaluate(ReceivedInvoice, string)` is non-nullable on `pattern`, and `ReceivedInvoice.CompanyVat` is a non-nullable `string` (default `string.Empty`) — matching the spec's description exactly. The implementation is defensively null-tolerant via `?.Trim()`, so null-input tests are legitimate even though the static signature says non-nullable; this mirrors the existing precedent in `CompanyNameClassificationRuleTests` (`companyName!`, `pattern!` with `[InlineData(null)]`).

Integration point: `InvoiceClassificationFixtures.CreateInvoice(...)` (`backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/TestHelpers/InvoiceClassificationFixtures.cs`) is the shared builder already used by `AmountClassificationRuleTests` and `CompanyNameClassificationRuleTests`. It currently has no `companyVat` parameter — this is the one shared piece of test infrastructure this task touches, and it is additive/backward-compatible only.

## Proposed Architecture

### Component Overview

No new components. One new leaf test file plugs into the existing test tree; one shared test helper gains an additional optional parameter.

```
backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/
├── Rules/
│   ├── AmountClassificationRuleTests.cs        (existing, unchanged)
│   ├── CompanyNameClassificationRuleTests.cs   (existing, unchanged)
│   ├── DescriptionClassificationRuleTests.cs   (existing, unchanged)
│   ├── ItemDescriptionClassificationRuleTests.cs (existing, unchanged)
│   └── VatClassificationRuleTests.cs           (NEW)
└── TestHelpers/
    └── InvoiceClassificationFixtures.cs        (MODIFIED: +companyVat param)
```

No changes to `Anela.Heblo.Domain`, DI wiring, or the test project's `.csproj` (xUnit + FluentAssertions are already referenced).

### Key Design Decisions

#### Decision 1: Extend the shared fixture vs. construct `ReceivedInvoice` inline

**Options considered:**
- (a) Add an optional `companyVat` parameter to `InvoiceClassificationFixtures.CreateInvoice(...)`.
- (b) Construct `ReceivedInvoice` object-initializer instances directly inside `VatClassificationRuleTests`, bypassing the fixture entirely.

**Chosen approach:** (a) — extend the fixture.

**Rationale:** `AmountClassificationRuleTests` and `CompanyNameClassificationRuleTests` both build their invoices exclusively through `InvoiceClassificationFixtures.CreateInvoice`, establishing that as the project convention for this test area. Bypassing it for the new file would fork the pattern for no reason and make the new test class look inconsistent with its siblings on inspection/diff — which the spec (FR-1) explicitly calls out as a review criterion. Extending the fixture is low-risk: the new parameter is optional with a default (`""`) that preserves every existing call site verbatim (confirmed: all current call sites in the repo — `AmountClassificationRuleTests`, `CompanyNameClassificationRuleTests`, `DescriptionClassificationRuleTests`, `ItemDescriptionClassificationRuleTests`, `RuleEvaluationEngineTests` — use named arguments, so parameter order/position is irrelevant to them).

#### Decision 2: Parameter placement given the existing `params string[] itemNames` tail parameter

**Options considered:**
- (a) Insert `string companyVat = ""` before the existing `params string[] itemNames` parameter, i.e. as the fourth positional/named optional parameter.
- (b) Append after `itemNames` — not legal in C#, since `params` must be the last parameter in the signature.

**Chosen approach:** (a) — new signature:
```csharp
internal static ReceivedInvoice CreateInvoice(
    decimal totalAmount = 0m,
    string companyName = "",
    string description = "",
    string companyVat = "",
    params string[] itemNames)
```

**Rationale:** C# requires `params` to be the final parameter, so there is only one legal insertion point. Because every existing call site in the repo uses named arguments (verified via repo-wide grep), inserting a new optional parameter ahead of `itemNames` does not break any positional-argument call — there are none. This was independently verified, not assumed.

#### Decision 3: How to express null-`CompanyVat` test cases

**Options considered:**
- (a) Widen the fixture parameter to `string? companyVat`, allowing `CreateInvoice(companyVat: null)`.
- (b) Keep the fixture parameter non-nullable (matching `ReceivedInvoice.CompanyVat`'s actual non-nullable-with-default-empty type) and construct the one or two null-`CompanyVat` cases via a direct `new ReceivedInvoice { CompanyVat = null! }` in the test file, exactly as `CompanyNameClassificationRuleTests.Evaluate_NullOrWhitespaceCompanyName_ReturnsFalse` does with `companyName!`.

**Chosen approach:** (b).

**Rationale:** `ReceivedInvoice.CompanyVat` is declared as non-nullable `string`; making the fixture parameter `string?` would be a wider, less faithful abstraction than the domain type it wraps, and would diverge from how `companyName`/`description` are already typed in the same fixture (both non-nullable `string`). The one or two null-`CompanyVat` cases are edge cases, not the common path — precedent in `CompanyNameClassificationRuleTests` already establishes constructing directly with a null-forgiving `!` as the idiomatic way to hit this in this codebase. This keeps the fixture's public contract simple and consistent with its siblings while still letting the test class cover the null path.

## Implementation Guidance

### Directory / Module Structure

- New file: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/VatClassificationRuleTests.cs`
  - Namespace: `Anela.Heblo.Tests.Features.InvoiceClassification.Rules`
  - `using Anela.Heblo.Domain.Features.InvoiceClassification.Rules;`
  - `using Anela.Heblo.Tests.Features.InvoiceClassification.TestHelpers;`
  - `using FluentAssertions;`
  - `using Xunit;`
  - `public class VatClassificationRuleTests { private readonly VatClassificationRule _sut = new(); ... }`
- Modified file: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/TestHelpers/InvoiceClassificationFixtures.cs`
  - Add `string companyVat = ""` parameter to `CreateInvoice`, positioned before `params string[] itemNames` (the only legal position).
  - Set `CompanyVat = companyVat` in the returned `ReceivedInvoice` initializer.

No other files change. No new project references, no `.csproj` edits, no DI/test-runner configuration changes.

### Interfaces and Contracts

`InvoiceClassificationFixtures.CreateInvoice` new signature (only change to any shared contract):

```csharp
internal static ReceivedInvoice CreateInvoice(
    decimal totalAmount = 0m,
    string companyName = "",
    string description = "",
    string companyVat = "",
    params string[] itemNames)
```

`VatClassificationRule.Evaluate(ReceivedInvoice, string)` — unchanged, treated as the fixed contract under test. Do not widen `pattern` to `string?`; the spec explicitly puts that out of scope (a separate nullability-annotation cleanup), and this review agrees — changing the interface signature is a production-code change with a blast radius across all five rule implementations and is unnecessary to achieve full coverage of the existing behavior.

### Data Flow

Standard AAA unit test, no infrastructure:

1. **Arrange** — build a `ReceivedInvoice` either via `InvoiceClassificationFixtures.CreateInvoice(companyVat: "...")` (the common path) or via a direct `new ReceivedInvoice { CompanyVat = null! }` (the null-`CompanyVat` edge case only).
2. **Act** — call `_sut.Evaluate(invoice, pattern)` directly; no mocking, no DI container, no async.
3. **Assert** — `result.Should().BeTrue()` / `.BeFalse()`.

Recommend consolidating the match/mismatch/whitespace/empty cases (FR-3, FR-4, FR-6) into one or two `[Theory]`/`[InlineData]` blocks with `(companyVat, pattern, expected)` tuples, following the `AmountClassificationRuleTests.Evaluate_OperatorBoundary_ReturnsExpected` precedent — this keeps the file compact while still traceable to each FR via inline comments grouping the `InlineData` rows (as that file does with `// >=`, `// <=` comment groups). Keep the null-`CompanyVat` case(s) (FR-5) as separate `[Fact]`s since they require the direct-construction path rather than the fixture, so they can't share a `[Theory]` with the fixture-based cases without restructuring around a null-tolerant fixture (rejected in Decision 3).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Fixture signature change breaks an existing call site relying on positional (unnamed) arguments | Low | Verified via repo-wide grep: every existing call to `CreateInvoice` uses named arguments exclusively. No positional calls exist today. Re-run `dotnet build` on the test project after the change to confirm — cheap, deterministic check. |
| New parameter inserted in the wrong position relative to `params itemNames` causes a compile error | Low | C# enforces `params` must be last; there is exactly one legal insertion point, documented above. |
| Test-name / structural drift from sibling files reduces reviewability | Low | Spec's suggested test inventory (FR list, method names) already mirrors sibling naming (`MethodUnderTest_Scenario_ExpectedResult`); follow it as given rather than inventing new naming. |
| Coverage tool double-counts trivial property getters (`Identifier`, `DisplayName`, `Description`) as "still uncovered" if no test ever instantiates and reads them | Very Low | Any test constructing `_sut` and calling `Evaluate` already exercises the class; if the coverage tool requires explicit property reads, add one trivial `[Fact]` asserting `_sut.Identifier == "ICO"` etc. — cheap insurance, not required by the spec's NFR-4 but worth a 2-minute check against actual coverage output before declaring 100%. |

## Specification Amendments

None required. The spec (`spec.r1.md`) is accurate against the current codebase state as verified:
- `VatClassificationRule.cs` content matches the spec's quoted snippet exactly.
- `ReceivedInvoice.CompanyVat` is confirmed non-nullable `string` with `= string.Empty` default, as stated.
- `IClassificationRule.Evaluate(ReceivedInvoice, string)` signature confirmed non-nullable `pattern`, as stated.
- The four sibling test files and their conventions (namespace, `_sut` field naming, FluentAssertions/xUnit usage, `[Theory]`/`[InlineData]` consolidation precedent in `AmountClassificationRuleTests`, and the `!`-suppressed-null precedent in `CompanyNameClassificationRuleTests`) all confirmed present and consistent with the spec's description.
- `InvoiceClassificationFixtures.CreateInvoice`'s current signature (no `companyVat` parameter, `params string[] itemNames` as the trailing parameter) confirmed exactly as the spec implies in FR-2.

One clarification worth flagging to the implementer (not a spec defect): the spec's FR-2 says "extend the fixture with an additional optional parameter" without specifying where in the parameter list. Because `itemNames` is a `params` array, `companyVat` must go immediately before it — this review pins that placement explicitly (Decision 2) to avoid an implementer discovering the constraint via a compile error.

## Prerequisites

None. No migrations, no config, no new infrastructure, no feature flags. The test project, xUnit, and FluentAssertions are already wired up and referenced; the implementer can start immediately by creating the new test file and making the one-line-equivalent fixture extension.
