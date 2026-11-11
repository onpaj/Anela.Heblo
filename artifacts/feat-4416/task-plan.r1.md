# Implementation Plan: Regex timeout in InvoiceClassification rules

Goal: bound regex evaluation to 200 ms in the three regex rules; timeout = non-match.
Test project: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/`.

### task: add-regex-helper-and-tests
Files: create `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/ClassificationRegex.cs`; modify the three rule files and the three `*RuleTests.cs`.

- [ ] Step 1 (TDD): In each of `CompanyNameClassificationRuleTests`, `DescriptionClassificationRuleTests`, `ItemDescriptionClassificationRuleTests` add a test: pattern `(a+)+$`, input `new string('a', 40) + "!"` in the relevant field; assert `Evaluate` returns false and a Stopwatch elapsed < 5 s. Also a test that an invalid pattern (`[abc`) still falls back to Contains. Run `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~InvoiceClassification.Rules"`; the timeout tests should hang/fail before the fix.
- [ ] Step 2: Create `ClassificationRegex.cs` exactly as in `design.r1.md` (Timeout 200 ms; catch `RegexMatchTimeoutException` -> false; catch `ArgumentException` -> Contains).
- [ ] Step 3: In `CompanyNameClassificationRule` and `DescriptionClassificationRule`, replace the try/catch with `return ClassificationRegex.IsMatchOrContains(invoice.<Field>, pattern);` keeping the guard; remove the unused `using System.Text.RegularExpressions;`.
- [ ] Step 4: In `ItemDescriptionClassificationRule.EvaluateItemDescription`, delegate likewise.
- [ ] Step 5: Run `dotnet build backend`, `dotnet format backend --verify-no-changes` (or `dotnet format`), and the InvoiceClassification tests; all must pass.
- [ ] Step 6: Commit `fix(invoice-classification): add regex match timeout to classification rules (#4416)`.

- [ ] Step 7: Process docs: `docs/processes/feed-invoice-classification.md` and `module-invoice-classification.md` likely own these files. Add a Known quirks note (regex timeout 200 ms, timeout = non-match), bump `verified_at`, run `python3 scripts/process-docs/check.py index`, and commit in the same PR.
