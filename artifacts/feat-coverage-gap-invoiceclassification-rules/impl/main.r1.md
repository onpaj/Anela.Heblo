All tasks complete. Here's the summary:

---

## Implementation Complete

**86 tests passing** (69 new + 17 pre-existing), `dotnet format` clean, build error-free.

### Files created (6 new, 0 modified)

| File | Tests | Coverage target |
|---|---|---|
| `TestHelpers/InvoiceClassificationFixtures.cs` | — (helper) | — |
| `Rules/AmountClassificationRuleTests.cs` | 26 | All 6 operator branches |
| `Rules/CompanyNameClassificationRuleTests.cs` | 12 | Regex + `"["` fallback |
| `Rules/DescriptionClassificationRuleTests.cs` | 11 | Regex + `"["` fallback |
| `Rules/ItemDescriptionClassificationRuleTests.cs` | 12 | Item iteration + per-item fallback |
| `Services/RuleEvaluationEngineTests.cs` | 7 | Ordering, filter, short-circuit |

### Notable design choices honored
- `"["` used exclusively for fallback tests (guaranteed `ArgumentException` on all .NET versions); sanity `[Fact]` guards against future framework drift
- `AmountClassificationRule` `catch` block left uncovered by design — it's unreachable through public inputs (`decimal.TryParse` never throws), documented in test comment
- Short-circuit test uses `Mock.Verify(..., Times.Never)` on later-ordered strategies — the only way to prove early exit

### Commits
- `b82792d0` fixtures helper
- `9834eb4a` Amount tests
- `e8fb8a49` CompanyName tests
- `9771bd61` Description tests
- `4a08f648` ItemDescription tests
- `eeabcb7b` RuleEvaluationEngine tests