# Specification: Unit test coverage for `VatClassificationRule.Evaluate`

## Summary
Add a dedicated xUnit test class covering `VatClassificationRule.Evaluate` in the `Anela.Heblo.Domain` invoice-classification rules. The method has 0% line coverage today despite being a live rule used to auto-route supplier invoices to accounting templates by IČO (Czech company VAT/registration ID). This is a pure test-authoring task — no production code changes.

## Background
`VatClassificationRule` (`backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/VatClassificationRule.cs`) implements `IClassificationRule` and is one of five sibling rule types (`AmountClassificationRule`, `CompanyNameClassificationRule`, `DescriptionClassificationRule`, `ItemDescriptionClassificationRule`) that a `ReceivedInvoice` is evaluated against to select an accounting template. All four sibling rules already have test coverage under `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/`; `VatClassificationRuleTests.cs` is the missing one. Because this rule is used unattended in production invoice routing, an untested regression (e.g., a comparison-logic change that silently flips match/no-match behavior) would misroute every invoice from an affected vendor with no immediate alert.

The current implementation:

```csharp
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

`ReceivedInvoice.CompanyVat` is a non-nullable `string` property (default `string.Empty`) on the domain model, but the `Evaluate` signature accepts `pattern` as `string` (not `string?`) even though the implementation null-conditionally handles a null `CompanyVat`/`pattern`. Tests must exercise both the realistic non-null-but-possibly-empty case and the defensive null-handling case, since `string.Equals(string?, string?, StringComparison)` is a valid overload and callers (e.g., a rule engine reading a nullable `pattern` from storage) could pass null.

## Functional Requirements

### FR-1: Test class location and structure
Add `VatClassificationRuleTests.cs` under `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/`, mirroring the existing sibling test files in that folder (`AmountClassificationRuleTests.cs`, `CompanyNameClassificationRuleTests.cs`, `DescriptionClassificationRuleTests.cs`, `ItemDescriptionClassificationRuleTests.cs`) for namespace, naming, and structural conventions:
- Namespace: `Anela.Heblo.Tests.Features.InvoiceClassification.Rules`
- Class: `public class VatClassificationRuleTests`
- System under test held as `private readonly VatClassificationRule _sut = new();`
- Use `FluentAssertions` (`result.Should().Be(expected)` / `BeTrue()` / `BeFalse()`) and `Xunit` (`[Fact]`, `[Theory]`/`[InlineData]`), matching sibling files.
- Build invoices via the shared `InvoiceClassificationFixtures.CreateInvoice(...)` helper in `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/TestHelpers/InvoiceClassificationFixtures.cs`, consistent with how `AmountClassificationRuleTests` and `CompanyNameClassificationRuleTests` build fixtures — see FR-2 for the required fixture change this implies.

**Acceptance criteria:**
- New file exists at `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/VatClassificationRuleTests.cs`.
- File compiles as part of the existing `Anela.Heblo.Tests` project with no new project references required.
- Class/namespace/style match the four sibling rule test files (reviewable by inspection/diff).

### FR-2: Fixture support for `CompanyVat`
`InvoiceClassificationFixtures.CreateInvoice(...)` currently accepts `totalAmount`, `companyName`, `description`, and `itemNames`, but has no parameter for `CompanyVat`. Extend the fixture with an additional optional parameter, e.g. `string companyVat = ""`, defaulted to preserve every existing call site's behavior, and set it on the constructed `ReceivedInvoice`. Alternatively, tests may construct `ReceivedInvoice` instances directly (as `CompanyNameClassificationRuleTests`' null/whitespace cases do implicitly through the fixture) — but extending the shared fixture is preferred to keep this test class consistent with its siblings and reusable for future tests.

**Acceptance criteria:**
- `InvoiceClassificationFixtures.CreateInvoice` gains a `companyVat` optional parameter (or equivalent) that sets `ReceivedInvoice.CompanyVat`.
- All pre-existing calls to `CreateInvoice` across the test suite continue to compile and pass unchanged (default value preserves current behavior).

### FR-3: Case-sensitivity and exact-match coverage
Test that `Evaluate` returns `true` for a `CompanyVat` that exactly matches `pattern`, and for matches that differ only by letter case (exercising `StringComparison.OrdinalIgnoreCase`).

**Acceptance criteria:**
- A test with identical `CompanyVat` and `pattern` values (e.g., `"12345678"` vs `"12345678"`) returns `true`.
- A test with a case-varied match (e.g., `CompanyVat` containing letters, since IČO is normally numeric — use a representative alphanumeric example such as `"CZ12345678"` vs `"cz12345678"`, or note that pure-numeric IČO has no case variance and case-insensitivity is still verified via a synthetic alphanumeric pattern) returns `true`.
- A definitely non-matching IČO (e.g., `CompanyVat = "12345678"`, `pattern = "87654321"`) returns `false`.

### FR-4: Whitespace-trimming coverage
Test that leading/trailing whitespace on either `CompanyVat` or `pattern` (or both) does not affect the match outcome — the comparison must be equivalent to the trimmed values being equal.

**Acceptance criteria:**
- `CompanyVat = "  12345678  "`, `pattern = "12345678"` returns `true`.
- `CompanyVat = "12345678"`, `pattern = "  12345678  "` returns `true`.
- `CompanyVat = " 12345678 "`, `pattern = " 12345678 "` returns `true`.
- Internal whitespace (not leading/trailing) is NOT trimmed away — e.g., `CompanyVat = "1234 5678"`, `pattern = "12345678"` returns `false` (documents that only outer whitespace is ignored, per `Trim()` semantics).

### FR-5: Null-safety coverage
Test that a null `CompanyVat` and/or a null `pattern` does not throw, consistent with the null-conditional (`?.Trim()`) usage in the implementation, and returns the correct boolean per equality semantics (`string.Equals(null, null, ...)` is `true`; `string.Equals(null, "x", ...)` is `false`).

**Acceptance criteria:**
- `CompanyVat = null`, `pattern = null` → returns `true`, and the call does not throw.
- `CompanyVat = null`, `pattern = "12345678"` → returns `false`, does not throw.
- `CompanyVat = "12345678"`, `pattern = null` → returns `false`, does not throw.
- Because `ReceivedInvoice.CompanyVat` is typed as non-nullable `string`, the null-`CompanyVat` case must be exercised either by constructing a `ReceivedInvoice` directly with `CompanyVat = null!` (suppressing the nullable warning, matching the precedent in `CompanyNameClassificationRuleTests.Evaluate_NullOrWhitespaceCompanyName_ReturnsFalse` which uses `companyName!`) or by extending the fixture to accept `string? companyVat`. The null-`pattern` case is directly expressible since `Evaluate`'s `pattern` parameter is declared `string` but the method body treats it null-safely — pass `null!` at the call site to exercise this without a compiler warning, matching the precedent in `CompanyNameClassificationRuleTests.Evaluate_NullOrWhitespacePattern_ReturnsFalse`.

### FR-6: Empty and whitespace-only value coverage
Test empty-string and whitespace-only values for both `CompanyVat` and `pattern`, since these are realistic degenerate inputs distinct from null.

**Acceptance criteria:**
- `CompanyVat = ""`, `pattern = ""` → returns `true` (both trim to empty, `Equals("", "")` is `true`).
- `CompanyVat = "   "`, `pattern = ""` → returns `true` (both trim to empty).
- `CompanyVat = "12345678"`, `pattern = ""` → returns `false`.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a pure in-memory unit test suite with no I/O, no async, no infrastructure dependencies. Each test should execute in well under 10ms; the whole new test class should add negligible time to the existing test run.

### NFR-2: Security
Not applicable — no secrets, no external calls, no data persistence involved. Test data uses synthetic, non-real IČO values.

### NFR-3: Determinism
All tests must be fully deterministic (no time-based, random, or environment-dependent values) and safe to run in parallel with the rest of the `Anela.Heblo.Tests` suite, consistent with existing sibling rule tests.

### NFR-4: Coverage target
The new tests must bring `VatClassificationRule.cs` line coverage to 100% (the class has a single 3-line method body plus three trivial expression-bodied properties already exercised implicitly by any test that constructs the class), well above the 60% filter threshold cited in the coverage gap report.

## Data Model
No data model changes. Relevant existing types (unchanged):
- `ReceivedInvoice` (`backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoice.cs`) — the `CompanyVat` property (`string`, default `string.Empty`) is the field under test.
- `IClassificationRule` (`backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationRule.cs`) — the interface `VatClassificationRule` implements; defines `Identifier`, `DisplayName`, `Description`, and `Evaluate(ReceivedInvoice, string)`.
- `VatClassificationRule` — the class under test, unchanged by this work.

The only data-model-adjacent change is the fixture extension described in FR-2 (test helper, not production code).

## API / Interface Design
No API changes. This work adds test code only:
- New file: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/VatClassificationRuleTests.cs`
- Modified file: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/TestHelpers/InvoiceClassificationFixtures.cs` (add `companyVat` parameter to `CreateInvoice`, per FR-2)

Suggested test method inventory (naming convention follows sibling files' `MethodUnderTest_Scenario_ExpectedResult` pattern):
- `Evaluate_ExactMatch_ReturnsTrue`
- `Evaluate_CaseInsensitiveMatch_ReturnsTrue`
- `Evaluate_NonMatchingVat_ReturnsFalse`
- `Evaluate_LeadingTrailingWhitespaceOnCompanyVat_IsIgnored_ReturnsTrue`
- `Evaluate_LeadingTrailingWhitespaceOnPattern_IsIgnored_ReturnsTrue`
- `Evaluate_LeadingTrailingWhitespaceOnBoth_IsIgnored_ReturnsTrue`
- `Evaluate_InternalWhitespaceDifference_ReturnsFalse`
- `Evaluate_NullCompanyVatAndNullPattern_ReturnsTrue`
- `Evaluate_NullCompanyVat_ReturnsFalse`
- `Evaluate_NullPattern_ReturnsFalse`
- `Evaluate_EmptyCompanyVatAndEmptyPattern_ReturnsTrue`
- `Evaluate_WhitespaceOnlyCompanyVatTrimsToEmpty_MatchesEmptyPattern_ReturnsTrue`
- `Evaluate_NonEmptyCompanyVatAgainstEmptyPattern_ReturnsFalse`

`[Theory]`/`[InlineData]` may be used to consolidate related cases (e.g., combine FR-3's match/mismatch cases into one theory with `(companyVat, pattern, expected)` tuples), following the `AmountClassificationRuleTests.Evaluate_OperatorBoundary_ReturnsExpected` precedent, as long as each distinct behavior in FR-3–FR-6 is still individually traceable in a test name or inline comment.

## Dependencies
- Existing test project `Anela.Heblo.Tests` (xUnit, FluentAssertions) — already referenced, no new package dependencies.
- `InvoiceClassificationFixtures` test helper — extended per FR-2, no new dependency.
- No changes to `VatClassificationRule.cs` itself, `ReceivedInvoice.cs`, or any production code/DI wiring.

## Out of Scope
- Any change to `VatClassificationRule`'s production behavior or signature.
- Integration/end-to-end tests of the invoice classification pipeline (`RuleEvaluationEngine`, `ClassificationRuleRepository`, etc.) — those already have their own test coverage elsewhere in the suite and are unaffected.
- Widening `Evaluate`'s `pattern` parameter to `string?` at the interface/implementation level — the spec tests around the existing (non-nullable-annotated but null-tolerant) signature as-is; a nullability-annotation cleanup is a separate concern.
- Coverage of the other four classification rule types — already covered by existing tests.
- CI/coverage-gate configuration changes (e.g., adjusting the 60% threshold) — this task only needs to raise this file's actual coverage, not the gate mechanism.

## Open Questions
None.

## Status: COMPLETE
