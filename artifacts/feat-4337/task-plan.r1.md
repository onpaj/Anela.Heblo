# VatRateCalculator.FromPrices Unit Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a unit test suite for `VatRateCalculator.FromPrices` that covers both its branches (fallback to 21% and the VAT-recovery formula) and its rounding behavior, closing the coverage gap reported in issue #4337.

**Architecture:** One new pure-function test file, `VatRateCalculatorTests.cs`, added to the existing `backend/test/Anela.Heblo.Tests/Features/ProductPricing/` folder. No production code changes. A single `[Theory]`/`[InlineData]` test method covers all cases, matching the style of the sibling `PriceComparisonDqtAdapterTests.cs` already in that folder.

**Tech Stack:** .NET 8, xUnit (`[Theory]`/`[InlineData]`), FluentAssertions.

---

### task: add-vatratecalculator-unit-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/ProductPricing/VatRateCalculatorTests.cs`

- [ ] **Step 1: Write the failing test file**

Create `backend/test/Anela.Heblo.Tests/Features/ProductPricing/VatRateCalculatorTests.cs` with the following content:

```csharp
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class VatRateCalculatorTests
{
    [Theory]
    // FR-1: priceWithoutVat <= 0 falls back to the standard rate, regardless of priceWithVat.
    [InlineData(100, 0, 21)]
    [InlineData(100, -10, 21)]
    // FR-2: formula branch reaching the standard rate (121/100 - 1 = 0.21 -> 21%).
    [InlineData(121, 100, 21)]
    // FR-3: formula branch computing a reduced rate (115/100 - 1 = 0.15 -> 15%).
    [InlineData(115, 100, 15)]
    // FR-4: rounding edge. (211/200 - 1) * 100 = 5.5 exactly. Math.Round(5.5m, 0) uses
    // MidpointRounding.ToEven (banker's rounding) by default, so 5.5 rounds to 6 (the
    // nearest even integer), not 5. This value was verified against .NET's actual
    // Math.Round(decimal, int) semantics, not hand-derived by assumption.
    [InlineData(211, 200, 6)]
    public void FromPrices_ReturnsExpectedVatRate(decimal priceWithVat, decimal priceWithoutVat, decimal expected)
    {
        // Act
        var result = VatRateCalculator.FromPrices(priceWithVat, priceWithoutVat);

        // Assert
        result.Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run the tests to verify they pass**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~VatRateCalculatorTests"
```
Expected: all 5 `[InlineData]` cases of `FromPrices_ReturnsExpectedVatRate` PASS (this is new test-only code against existing, correct production logic, so it is expected to pass on the first run — there is no implementation step, only verification).

If the `211, 200, 6` case fails with an actual value of `5`, do not "fix" the test by hand-waving — first confirm with a quick scratch check what `Math.Round(5.5m, 0)` actually returns on this runtime (it must be `6` under the default `MidpointRounding.ToEven`); if it genuinely returns something else, stop and re-verify the .NET rounding semantics before changing the expected value.

- [ ] **Step 3: Run `dotnet format` and the full coverage-gap-relevant test filter**

Run:
```bash
cd backend
dotnet format --verify-no-changes
dotnet build
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ProductPricing"
```
Expected: build succeeds, `dotnet format` reports no changes needed, and every test in the `Features/ProductPricing` folder (including the new `VatRateCalculatorTests`) passes.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/ProductPricing/VatRateCalculatorTests.cs
git commit -m "test(product-pricing): cover VatRateCalculator.FromPrices branches and rounding"
```

## Self-Review

**1. Spec coverage:**
- FR-1 (non-positive `priceWithoutVat` fallback, both zero and negative) → covered by `InlineData(100, 0, 21)` and `InlineData(100, -10, 21)`.
- FR-2 (formula branch, standard rate) → covered by `InlineData(121, 100, 21)`.
- FR-3 (formula branch, reduced rate) → covered by `InlineData(115, 100, 15)`.
- FR-4 (rounding behavior on a non-whole-number raw result) → covered by `InlineData(211, 200, 6)`, with an inline comment explaining the `ToEven` rounding direction.
- NFR-1 (no mocks, pure function, fast) → satisfied: the test class has no fixture, no mocks, no I/O.
- NFR-2 (style consistency) → satisfied: same namespace convention, `[Theory]`/`[InlineData]` shape, and `FluentAssertions` usage as `PriceComparisonDqtAdapterTests.cs` in the same folder.

No gaps found — every FR and NFR maps to this one task.

**2. Placeholder scan:** No "TBD"/"TODO"/"implement later" markers. Every step shows the actual file content or exact command. No cross-references to code not defined in this plan.

**3. Type consistency:** The only type touched is the existing, unchanged `VatRateCalculator.FromPrices(decimal, decimal) : decimal` — no new types are introduced, so there is nothing to drift across tasks.
