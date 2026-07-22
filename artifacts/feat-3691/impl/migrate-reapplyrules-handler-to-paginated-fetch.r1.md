# Implementation: migrate-reapplyrules-handler-to-paginated-fetch

## What was implemented
Switched `ReapplyRulesHandler.Handle` from a single bulk `GetAllPhotosAsync` load to a paginated loop calling the new `GetPhotoRuleCandidatesPageAsync(pageSize, offset, cancellationToken)` repository method added in the previous task. A `PageSize = 2000` constant was added to the handler class. The per-photo tag-matching logic inside the loop body is unchanged — only the data source and iteration structure changed, from a single `foreach` over an in-memory list to a `while (true)` loop that fetches successive pages and breaks when a page returns fewer than `PageSize` rows. `GetAllPhotosAsync` itself was left in place on the repository (removal is a later task) — it is simply no longer called by this handler.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs` — added `PageSize` constant; replaced the single `GetAllPhotosAsync` bulk load + `foreach` with a paginated `while` loop over `GetPhotoRuleCandidatesPageAsync`.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/ReapplyRulesHandlerTests.cs` — replaced `GetAllPhotosAsync` mock setups with `GetPhotoRuleCandidatesPageAsync` mocks across all four photo-data tests; replaced the `PhotoAt` helper (built full `Photo` entities) with a `CandidateAt` helper that builds `PhotoAutoTagCandidate` projections directly, since the handler now only ever sees the projection type.

## Tests
- `ReapplyRulesHandlerTests` (mocked repository) — 6/6 passed: `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`.
- `ReapplyRulesBehaviorPreservationTests` (real `PhotobankRepository` against EF Core InMemory `ApplicationDbContext`, unmodified by this task) — 5/5 passed unchanged: `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5`. This exercises the new paginated loop end-to-end and proves output equivalence with the previous bulk-load behavior (`ManualTagWins_RuleTagNotInsertedOverSharedPk`, `DuplicateMatch_AddsOneRow_PhotosUpdatedCountsPhotosNotTags`, `EmptyActiveRules_RemovesAllRuleTags_AndReturnsZero`, `ScopedReapply_OnlyTouchesTargetRuleTag`, `DoubleApply_NoNewTags_IsIdempotent_AndDoesNotThrow`).
- Whole-solution build: `Build succeeded.`, `0 Error(s)` (13 pre-existing warnings unrelated to this change; one pre-existing MSB3073/exit-134 warning from the sandboxed `AccessMatrixGen` post-build step, unrelated to Photobank).

## How to verify
```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReapplyRulesHandlerTests"
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~ReapplyRulesBehaviorPreservationTests"
dotnet build Anela.Heblo.sln
```

## Notes
Implementation followed the task-context file's exact before/after snippets. No deviations.

## Status
DONE
