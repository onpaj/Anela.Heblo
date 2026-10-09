## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/UpdateClassificationRuleHandlerTests.cs:47` — the second not-found test swallows the exception via try/catch; `ThrowAsync` plus `Verify` would be more idiomatic. Non-blocking.

Notes: test-only diff; matches spec FR-1..FR-3 against the real handler and `ClassificationRule.Update`. Reviewed statically: the solution does not currently build on origin/main (`RecurringJobSeeder.cs(51,45): error CS1503`, unrelated to this feature), so the tests could not be executed in this run.
