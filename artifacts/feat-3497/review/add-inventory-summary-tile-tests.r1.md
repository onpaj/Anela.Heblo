# Code Review: Unit test coverage for InventorySummaryTileBase age-bucket logic

## Summary
All 7 required test methods exist with the correct names in `InventorySummaryTileBaseTests.cs`, currently pass (7/7, confirmed via `dotnet test`), and no production file was modified (confirmed via `git diff origin/main...HEAD` and a byte-for-byte restore check after mutation testing). However, empirical mutation testing on the actual production comparison operators shows the suite does **not** satisfy the acceptance criterion "fail if any bucket comparison operator... is reverted/mutated": two of the four operators can be flipped without any test failing.

## Review Result: REVISION_NEEDED

### task: add-inventory-summary-tile-tests
**Status:** REVISION_NEEDED
**Issues:**
- Mutation of the `recent` bucket's `<` to `<=` (line 43, `TotalDays < ThresholdCritical`) is not caught by any test. Production code captures its own `DateTime.UtcNow` at Act time, always fractionally later than the test's Arrange-time `now`, so an item created at `now.AddDays(-180)` is always measured as slightly more than 180.000 days elapsed — never exactly 180. `LoadDataAsync_ItemAt180Days_CountsAsMedium` never actually exercises the boundary value from the "recent" side.
- Mutation of the `medium` bucket's upper bound `<=` to `<` (line 47, `TotalDays <= ThresholdWarning`) is also not caught. The flakiness fix (nudging the `-365 days` fixture by `+1 second`) pushes the tested elapsed time to roughly 364.99999 days, comfortably inside `<365` too, so it can no longer distinguish `<=365` from `<365`.
- Both gaps stem from the same structural issue: with no injectable clock in `InventorySummaryTileBase.LoadDataAsync` (it calls `DateTime.UtcNow` directly), it is not possible to reliably land exactly on an integer-day boundary from a test — the elapsed value always drifts forward by whatever wall-clock time passes between Arrange and Act.

## Docs to Update
None.

## Overall Notes
- Confirmed via `git diff origin/main...HEAD -- . ':(exclude)artifacts'`: only the new test file was added; no production file was touched.
- `ProductInventorySummaryTile.ItemFilter`, the null/`never` bucket test, and the happy-path shape test all hold up under inspection and are not tautological.
- `dotnet build`/`dotnet format` were not independently re-verified in this review due to long build times in the sandbox; secondary check, doesn't change the verdict.

---

## Resolution (round 2)

Confirmed by direct inspection: this specific mutation (exact strict-vs-inclusive comparison operator at the day boundary, e.g. `<` vs `<=` at exactly 180.000 elapsed days) is **structurally unreachable** from a black-box test given the current production code, for any fixture construction — not just the ones used here. `InventorySummaryTileBase.LoadDataAsync` calls `DateTime.UtcNow` directly with no injectable clock, and any nonzero wall-clock time between a test's Arrange step and the production Act step guarantees the measured elapsed time is *never* exactly on an integer-day boundary; it always drifts by a small positive epsilon. Because `<` and `<=` (or `>` and `>=`) only differ in behavior at that exact point, no black-box test can distinguish them without either (a) injecting a controllable clock into `InventorySummaryTileBase` — a production code change explicitly out of scope for this coverage-gap task — or (b) a global time-freezing mechanism not present in this codebase's test toolchain.

Given this constraint, a clarifying comment was added directly above the test class documenting the limitation, so future readers understand why the boundary tests verify "correct bucket to within one day" rather than "exact single-ULP operator." This is the practical ceiling of coverage achievable without expanding scope to include a production clock-injection change, which this task's brief and specification explicitly rule out. All other review findings (test authenticity, no production changes, correct bucket/null/total/shape behavior) hold.
