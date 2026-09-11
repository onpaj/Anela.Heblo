### task: add-vat-classification-rule-tests

Add `VatClassificationRuleTests.cs`, covering `VatClassificationRule.Evaluate` per FR-3 (case-sensitivity/exact-match), FR-4 (whitespace-trimming), FR-5 (null-safety), and FR-6 (empty/whitespace-only values) of the spec. Depends on `extend-invoice-classification-fixture` being done first (this task's `[Theory]` cases call `InvoiceClassificationFixtures.CreateInvoice(companyVat: ...)`).

**Step 1 — Create the test file with a failing/incomplete skeleton first, to confirm the harness wires up correctly.**

File: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/VatClassificationRuleTests.cs`

```csharp
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.InvoiceClassification.Rules;
using Anela.Heblo.Tests.Features.InvoiceClassification.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification.Rules;

public class VatClassificationRuleTests
{
    private readonly VatClassificationRule _sut = new();

    [Theory]
    // exact match
    [InlineData("12345678", "12345678", true)]
    // case-insensitive match (synthetic alphanumeric IČO — pure-numeric IČO has no case variance)
    [InlineData("CZ12345678", "cz12345678", true)]
    // non-match
    [InlineData("12345678", "87654321", false)]
    // leading/trailing whitespace — trimmed before comparison
    [InlineData("  12345678  ", "12345678", true)]
    [InlineData("12345678", "  12345678  ", true)]
    [InlineData(" 12345678 ", " 12345678 ", true)]
    // internal whitespace is NOT trimmed away — only outer whitespace is ignored
    [InlineData("1234 5678", "12345678", false)]
    // empty / whitespace-only values
    [InlineData("", "", true)]
    [InlineData("   ", "", true)]
    [InlineData("12345678", "", false)]
    public void Evaluate_MatchScenarios_ReturnsExpected(string companyVat, string pattern, bool expected)
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(companyVat: companyVat);

        // Act
        var result = _sut.Evaluate(invoice, pattern);

        // Assert
        result.Should().Be(expected);
    }
}
```

**Step 2 — Run just this test class and confirm it passes.**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~VatClassificationRuleTests"
```

Expected: `Passed! - Failed: 0, Passed: 10, Skipped: 0` (10 `[InlineData]` rows under one `[Theory]`).

**Step 3 — Add the null-safety `[Fact]`s (FR-5).**

These cannot go through the fixture-based `[Theory]` above because they need either a null `pattern` (fixture-built invoice is fine — `pattern` is passed straight to `Evaluate`) or a null `CompanyVat` (the fixture's `companyVat` parameter is non-nullable `string`, matching `ReceivedInvoice.CompanyVat`'s own non-nullable-with-default-empty type — per Decision 3 of the architecture review, null-`CompanyVat` cases construct `ReceivedInvoice` directly with `CompanyVat = null!`, mirroring the `companyName!` precedent in `CompanyNameClassificationRuleTests.Evaluate_NullOrWhitespaceCompanyName_ReturnsFalse`).

Add these three `[Fact]` methods inside `VatClassificationRuleTests`, after the `[Theory]` method from Step 1:

```csharp
    [Fact]
    public void Evaluate_NullPattern_ReturnsFalse()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(companyVat: "12345678");

        // Act
        var result = _sut.Evaluate(invoice, null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NullCompanyVat_ReturnsFalse()
    {
        // Arrange
        var invoice = new ReceivedInvoice { CompanyVat = null! };

        // Act
        var result = _sut.Evaluate(invoice, "12345678");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NullCompanyVatAndNullPattern_ReturnsTrue()
    {
        // Arrange
        var invoice = new ReceivedInvoice { CompanyVat = null! };

        // Act
        var result = _sut.Evaluate(invoice, null!);

        // Assert
        result.Should().BeTrue();
    }
```

These three facts require the `using Anela.Heblo.Domain.Features.InvoiceClassification;` line already present at the top of the file from Step 1 (needed for the `ReceivedInvoice` type used in direct construction).

**Step 4 — Run the full test class again and confirm all cases pass.**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~VatClassificationRuleTests"
```

Expected: `Passed! - Failed: 0, Passed: 13, Skipped: 0` (10 theory rows + 3 facts).

Sanity-check the boolean semantics being asserted, so a failure here is diagnosable rather than surprising:
- `Evaluate_NullPattern_ReturnsFalse`: `string.Equals("12345678", null, ...)` → `false`. Correct.
- `Evaluate_NullCompanyVat_ReturnsFalse`: `string.Equals(null, "12345678", ...)` → `false`. Correct.
- `Evaluate_NullCompanyVatAndNullPattern_ReturnsTrue`: `string.Equals(null, null, ...)` → `true`. Correct.

**Step 5 — Run the full backend test suite to confirm nothing else regressed.**

```bash
cd backend && dotnet test
```

Expected: all tests pass, including the pre-existing suite plus the 13 new `VatClassificationRuleTests` cases.

**Step 6 — Verify coverage of `VatClassificationRule.cs` reaches 100% (NFR-4).**

```bash
cd backend && dotnet test --collect:"XPlat Code Coverage"
```

Open the generated `coverage.cobertura.xml` (or equivalent report under `backend/test/Anela.Heblo.Tests/TestResults/<guid>/`) and confirm `VatClassificationRule.cs` shows 100% line coverage for the `Evaluate` method body. The three expression-bodied properties (`Identifier`, `DisplayName`, `Description`) are not directly invoked by any test in this class; per the spec's NFR-4 note, they are trivial one-line getters typically counted as covered by any test that constructs `_sut` under most coverage instrumentation (as sibling rule test classes — none of which explicitly assert on `Identifier`/`DisplayName`/`Description` either — already demonstrate for their own rule classes). If the coverage report shows any of these three properties as uncovered, add one trivial fact to close the gap:

```csharp
    [Fact]
    public void Properties_ReturnExpectedMetadata()
    {
        _sut.Identifier.Should().Be("ICO");
        _sut.DisplayName.Should().Be("IČO");
        _sut.Description.Should().Be("Porovnání IČO firmy");
    }
```

placed after the null-safety facts from Step 3. Re-run Step 6's coverage command if this fact is added, to confirm 100% is reached.

**Step 7 — Run `dotnet format` and `dotnet build` on the whole backend, per repo validation rules.**

```bash
cd backend && dotnet build
cd backend && dotnet format --verify-no-changes
```

If `dotnet format --verify-no-changes` reports formatting differences in the new/modified files, run `dotnet format` (without `--verify-no-changes`) to apply them, then re-run `dotnet build` and `dotnet test --filter "FullyQualifiedName~VatClassificationRuleTests"` to confirm the formatted file still compiles and passes.

**Step 8 — Commit.**

```bash
cd backend && git add test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/VatClassificationRuleTests.cs
git commit -m "Add VatClassificationRuleTests covering Evaluate match/whitespace/null/empty cases"
```

---

## Self-Review

**1. Spec coverage** — walking every FR in `spec.r1.md`:
- FR-1 (test class location/structure/namespace/`_sut`/FluentAssertions+xUnit/fixture usage): satisfied by `add-vat-classification-rule-tests` Step 1.
- FR-2 (fixture `companyVat` parameter, default preserves existing call sites): satisfied by `extend-invoice-classification-fixture`.
- FR-3 (exact match, case-insensitive match, non-match): the three `[InlineData]` rows `("12345678","12345678",true)`, `("CZ12345678","cz12345678",true)`, `("12345678","87654321",false)` in Step 1 of `add-vat-classification-rule-tests`.
- FR-4 (whitespace trimming, including internal-whitespace-not-trimmed): the four whitespace `[InlineData]` rows in the same `[Theory]`.
- FR-5 (null-safety, no throw): the three `[Fact]`s in Step 3 of `add-vat-classification-rule-tests` — each call is a direct, unguarded invocation, so any thrown exception fails the test automatically (FluentAssertions/xUnit convention — no `try/catch` needed to prove "does not throw").
- FR-6 (empty/whitespace-only): the three empty/whitespace `[InlineData]` rows in the `[Theory]`.
- NFR-1/NFR-2/NFR-3 (performance/security/determinism): satisfied structurally — pure in-memory synchronous tests, synthetic data, no shared mutable state between test methods (each `[Fact]`/`[Theory]` row constructs its own invoice).
- NFR-4 (100% coverage): addressed explicitly in Step 6 with a fallback property-test step if the coverage tool doesn't already count the expression-bodied properties as covered.
- All 13 suggested test names from the spec's "Suggested test method inventory" are represented either as a named `[Fact]` or as a distinct, commented `[InlineData]` row in the `[Theory]` — consistent with the spec's explicit allowance ("as long as each distinct behavior in FR-3–FR-6 is still individually traceable in a test name or inline comment").

**2. Placeholder scan** — no "TBD"/"TODO"/"handle appropriately" language anywhere in the two tasks; every step has literal, complete code and literal shell commands with stated expected output. No step says "similar to Task N" without inlining the actual code.

**3. Type consistency** — `CreateInvoice`'s new `companyVat` parameter (`extend-invoice-classification-fixture`, Step 2) is `string companyVat = ""`, and every call to it in `add-vat-classification-rule-tests` passes a `string` (never `null` — the null-`CompanyVat` cases correctly bypass the fixture and construct `ReceivedInvoice` directly, matching Decision 3 of the architecture review and avoiding a mismatch between a non-nullable fixture parameter and a null argument). `_sut.Evaluate(invoice, pattern)` is called with `(ReceivedInvoice, string)` everywhere, including the `null!`-suppressed calls, matching `IClassificationRule.Evaluate`'s actual signature confirmed from source. No drift found.
