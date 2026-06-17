Plan saved to `artifacts/feat-coverage-gap-invoiceclassification-rules/plan.r1.md`.

**Summary:** Seven tasks, all purely additive test code (no production changes):

1. Create `TestHelpers/InvoiceClassificationFixtures.cs` — `CreateInvoice` / `CreateRule` factories
2. `AmountClassificationRuleTests` — `[Theory]` matrix over all 6 operator branches + edge `[Fact]`s
3. `CompanyNameClassificationRuleTests` — regex hit/miss + `"["` fallback + null/whitespace guards + sanity check that `Regex.IsMatch("anything", "[")` still throws `ArgumentException`
4. `DescriptionClassificationRuleTests` — same shape as #3 against `Description`
5. `ItemDescriptionClassificationRuleTests` — items iteration, per-item fallback, mixed null/match safety
6. `RuleEvaluationEngineTests` — mock `IClassificationRule` strategies + real `ClassificationRule` data entities; covers ordering, active-filtering, no-match, unknown-identifier, sort-by-Order-not-insertion, and short-circuit via `Mock.Verify(..., Times.Never)` on later strategies
7. Full-suite validation, `dotnet format --verify-no-changes`, coverage spot-check

Each task is bite-sized (write tests → run → commit). Includes a spec-coverage map at the bottom showing where every FR/NFR/arch-amendment lands.