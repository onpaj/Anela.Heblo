## Review Result: PASS

### task: add-currency-filter-case-insensitive-theory
**Status:** PASS

**Verification performed:**
- Diffed the implemented test file against the exact content specified in `task-context/add-currency-filter-case-insensitive-theory.md`: identical (FR-1/FR-2/FR-3 unchanged, new `[Theory]` FR-4 test added verbatim).
- Confirmed `ShoptetApiInvoiceSource.GetAllAsync`'s list-mode currency filter uses `string.Equals(i.Price?.CurrencyCode, query.Currency, StringComparison.OrdinalIgnoreCase)`, so the two `InlineData` cases (`"czk"/"CZK"` and `"CZK"/"czk"`) genuinely exercise case-insensitivity in both directions rather than trivially passing.
- Ran `dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests"`: `Passed! - Failed: 0, Passed: 5, Skipped: 0` — all 5 executions (FR-1, FR-2, FR-3, FR-4 x2 InlineData) pass, no regressions.
- Acceptance criteria from the task context (add the theory, both InlineData cases pass, no regression in the rest of the class) are all met.

## Docs to Update
None — this is test-only coverage of existing, already-documented behavior; no public API, CLI, or operational behavior changed.
