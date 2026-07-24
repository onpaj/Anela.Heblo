# Code Review: Migrate ReapplyRulesHandler to Paginated Photo Fetch

## Summary
The implementation correctly migrates `ReapplyRulesHandler.Handle` from a single bulk `GetAllPhotosAsync` call to a paginated loop using `GetPhotoRuleCandidatesPageAsync`. The paginated loop structure is sound with proper termination logic (breaks when page.Count < PageSize, offset incremented by actual row count), the per-photo tag-matching logic remains unchanged, and all tests pass including behavior-preservation verification against real repository behavior.

## Review Result: PASS

### task: migrate-reapplyrules-handler-to-paginated-fetch
**Status:** PASS

## Overall Notes

**Paginated loop correctness:** The while loop correctly terminates and manages offsets:
- Initializes `offset = 0` and enters `while (true)`
- Fetches page with `GetPhotoRuleCandidatesPageAsync(PageSize, offset, cancellationToken)`
- Increments offset by `page.Count` (not fixed PageSize), avoiding row skipping/duplication
- Breaks when `page.Count < PageSize`, correctly handling the final partial page
- No infinite loop risk: offset always advances, and termination condition properly triggers

**Tag-matching logic:** The per-photo rule-matching code (lines 80-112) is structurally identical to the original, only indented one level deeper due to the paginated iteration pattern. All control flow paths (`continue` on no matches, deduplication via `addedPairs.Add()`, occupied-pair filtering, tag creation, `photosUpdated` counting) are preserved exactly.

**Test updates:** All four photo-data tests correctly migrated from `GetAllPhotosAsync` mocks returning `Photo` entities to `GetPhotoRuleCandidatesPageAsync` mocks returning `PhotoAutoTagCandidate` projections. The `PhotoAt` helper is properly removed (no longer needed), and the new `CandidateAt` helper directly builds the projection type. Both non-photo-data tests (`RuleNotFound_ReturnsError...`, `NoActiveRuleTagNames_...`) remain unaffected as expected.

**Verification:** 
- ReapplyRulesHandlerTests: 6/6 passed (mocked repository, validates new loop structure)
- ReapplyRulesBehaviorPreservationTests: 5/5 passed (real EF Core InMemory repository, proves end-to-end output equivalence with original bulk-load behavior)
- Full solution build: succeeded with 0 errors (97 pre-existing warnings unrelated to this change)

**Architecture compliance:** Maintains Clean Architecture patterns and MediatR handler structure. Repository method `GetAllPhotosAsync` left intact as specified (removal is a later task).
