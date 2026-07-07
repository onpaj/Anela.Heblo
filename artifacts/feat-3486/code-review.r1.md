## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `docs/features/product-margin-summary.md` — still documents the old, pre-refactor `TopProductCount`/"top N" behavior now that the parameter has been removed; not required by the spec (explicitly Out of Scope) but worth a follow-up doc pass.
- `frontend/src/api/generated/api-client.ts` — this regeneration also picked up unrelated pre-existing drift between the checked-in client and the current backend OpenAPI surface (`packaging_GetStatistics`, `DqtUnsupportedTestType`, `RunExpeditionListPrintFixResponse.skippedCount`, `RefreshTaskStatusDto.description` removal, `ArticleGenerationStepStatus` relocation). Verified via `git diff origin/main -- frontend/src/api/generated/api-client.ts` that this drift already existed on `main` before this branch — none of it was introduced by this change, and it is an unavoidable side effect of the mandatory "regenerate, don't hand-edit" workflow. No action needed for this PR; flagging only so a future generated-client refresh isn't mistaken for scope creep.

## Notes
Diff reviewed: `backend/.../GetProductMarginSummaryRequest.cs` (removes unused `TopProductCount` property), `frontend/src/api/hooks/useProductMarginSummary.ts` (drops the now-invalid positional argument), and the regenerated `frontend/src/api/generated/api-client.ts`. `GetProductMarginSummaryHandler`, `GenerateTopProducts`, the controller, and all existing tests are untouched, matching the architect's binding decision (Option 1: remove the dead parameter) and the task context's explicit statement that the handler requires zero changes. Confirmed via repo-wide grep that no reference to `TopProductCount`/`topProductCount` remains in any `.cs`/`.ts`/`.tsx` file. This is a pure, behavior-preserving dead-code removal — no correctness risk.
