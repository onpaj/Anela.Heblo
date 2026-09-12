## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

### Notes
Reviewed the full feature-branch diff (`origin/main`...`HEAD`) against `spec.r1.md`. Code changes are confined to test code:

- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/VatClassificationRuleTests.cs` (new, 88 lines) — a `[Theory]` covering exact match, case-insensitive match, non-match, leading/trailing-whitespace trimming (both sides, both directions), internal-whitespace non-trimming, and empty/whitespace-only values (FR-3, FR-4, FR-6), plus three `[Fact]`s for null-safety (null pattern, null `CompanyVat`, both null — FR-5) and one for the trivial metadata properties. Traced each `InlineData` row and `[Fact]` by hand against `VatClassificationRule.Evaluate`'s actual `string.Equals(x?.Trim(), y?.Trim(), StringComparison.OrdinalIgnoreCase)` logic — every expected value is correct.
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/TestHelpers/InvoiceClassificationFixtures.cs` — adds an optional `companyVat = ""` parameter to `CreateInvoice`, inserted before the trailing `params string[] itemNames` (the only legal position). Verified via repo-wide grep that every existing call site (`RuleEvaluationEngineTests`, `AmountClassificationRuleTests`, `CompanyNameClassificationRuleTests`, `DescriptionClassificationRuleTests`, `ItemDescriptionClassificationRuleTests`) uses named arguments or no arguments, so the new parameter cannot break positional-argument call sites (there are none).

Verification performed beyond static reading:
- `dotnet build` on the test project: 0 errors (pre-existing unrelated warnings only).
- `dotnet test --filter FullyQualifiedName~VatClassificationRuleTests`: 14/14 pass.
- `dotnet test --filter FullyQualifiedName~InvoiceClassification` (full module regression check for the shared-fixture edit): 108/111 pass; the 3 failures are `ClassificationRuleRepositoryReorderIntegrationTests` failing with "Docker is either not running or misconfigured" (Testcontainers/Postgres integration tests requiring Docker, unavailable in this review sandbox) — pre-existing environment limitation, unrelated to this diff (that test class never calls `CreateInvoice` or touches `VatClassificationRule`).

No production code was touched (`VatClassificationRule.cs` unchanged), matching the spec's "pure test-authoring task" framing and Out-of-Scope section. No reuse/simplification/efficiency issues worth flagging — the new test file mirrors sibling rule test files' structure and naming exactly, as FR-1 required.
