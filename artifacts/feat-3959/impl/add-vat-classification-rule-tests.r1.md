# Implementation: add-vat-classification-rule-tests

## What was implemented
Added `VatClassificationRuleTests.cs`, a dedicated xUnit test class covering `VatClassificationRule.Evaluate`, mirroring the structure and conventions of the four sibling rule test classes in the same folder. The class uses `InvoiceClassificationFixtures.CreateInvoice(companyVat: ...)` (extended by the prior task) for all non-null scenarios, and constructs `ReceivedInvoice` directly with `CompanyVat = null!` for the null-`CompanyVat` cases, per Decision 3 of the architecture review and the `companyName!` precedent in `CompanyNameClassificationRuleTests`.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/VatClassificationRuleTests.cs` (new) — `VatClassificationRuleTests` class with:
  - One `[Theory]` (`Evaluate_MatchScenarios_ReturnsExpected`) with 10 `[InlineData]` rows covering exact match, case-insensitive match (synthetic alphanumeric IČO), non-match, leading/trailing whitespace trimming on either or both sides, internal-whitespace-not-trimmed, and empty/whitespace-only values (FR-3, FR-4, FR-6).
  - Three `[Fact]`s covering null-safety (FR-5): `Evaluate_NullPattern_ReturnsFalse`, `Evaluate_NullCompanyVat_ReturnsFalse`, `Evaluate_NullCompanyVatAndNullPattern_ReturnsTrue`.
  - One `[Fact]` (`Properties_ReturnExpectedMetadata`) asserting `Identifier`/`DisplayName`/`Description`, added after confirming via a coverage run that these three expression-bodied properties were NOT auto-covered by the other tests (initial coverage run showed 50% line-rate on `VatClassificationRule.cs`, missing lines 5-7; this fact closes that gap).

## Tests
- `VatClassificationRuleTests` — 14 tests total (10 theory rows + 4 facts), all passing.

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~VatClassificationRuleTests"
```
Expected: `Passed! - Failed: 0, Passed: 14, Skipped: 0`.

Coverage verification (NFR-4):
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~VatClassificationRuleTests" --collect:"XPlat Code Coverage"
```
The generated `coverage.cobertura.xml` shows `line-rate="1"` (100%) for `Anela.Heblo.Domain.Features.InvoiceClassification.Rules.VatClassificationRule` — all 6 executable lines (the three property getters and the three lines of `Evaluate`) hit.

Full backend suite (regression check):
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Result: Total 6690, Passed 6581, Failed 105 (all pre-existing `System.ArgumentException: Docker is either not running or misconfigured` failures from Postgres-testcontainer integration tests — same 105 failures as the baseline established by the prior `extend-invoice-classification-fixture` task, unrelated to this change), Skipped 4. Confirms the +13 net new theory/fact executions (14 new tests minus the 1 property fact that wasn't in the original baseline count — see Notes) introduced no regressions.

Build and format:
```bash
cd backend && dotnet build            # 0 errors, pre-existing warnings only
cd .. && dotnet format Anela.Heblo.sln --verify-no-changes --include backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/VatClassificationRuleTests.cs   # no changes needed
```

## Notes
The task-context's Step 6 fallback (add `Properties_ReturnExpectedMetadata` if the coverage tool doesn't count the expression-bodied properties as covered) was needed: the initial coverage run confirmed `Identifier`/`DisplayName`/`Description` were uncovered by the 13 match/null-safety tests alone, so the 14th fact was added and coverage re-verified at 100%. No other deviations from the task spec.

## PR Summary
Added `VatClassificationRuleTests.cs` covering `VatClassificationRule.Evaluate`'s match, whitespace-trimming, null-safety, and empty-value branches (FR-3–FR-6), plus a property-metadata fact needed to reach 100% line coverage on the rule class. Full backend suite shows no regressions versus the established Docker-testcontainer-failure baseline.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/VatClassificationRuleTests.cs` — new test class, 14 tests

## Status
DONE
