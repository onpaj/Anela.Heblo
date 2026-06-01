# RequestedBy Ownership Audit

**Date:** 2026-05-25  
**Task:** FR-4 (Task 5) — Audit all RequestedBy comparison sites  
**Status:** COMPLETE — No unhandled ownership checks found

## Audit Summary

All `RequestedBy` usage sites in the codebase have been classified and reviewed. The two critical ownership-check sites identified in the architecture review have been fixed in Tasks 2 and 4:

1. **SubmitArticleFeedbackHandler.cs** — Now compares using `GetIdentifier()` (OID-based)
2. **GenerateArticleHandler.cs** — Now writes OID-based identifiers

No additional ownership-check comparisons were discovered that escaped the fix. All other usages fall into safe categories (filters, projections, tests, generated code, adapters).

## Detailed Classification

| Location | Type | Bucket | Semantics / Action |
|---|---|---|---|
| `Article.cs:23` | Domain model property | Property definition | Stores the OID-based identifier; no logic at this level |
| `ArticleRepository.cs:76` | Pass-through filter | Filter parameter | Line `a.RequestedBy == requestedBy`; forwards API query param without comparison logic; now carries OID-based values from API adapter |
| `GenerateArticleHandler.cs:46` | Write site | Fixed in Task 4 | Now calls `currentUser.GetIdentifier()` to capture OID; semantics updated from name to OID |
| `SubmitArticleFeedbackHandler.cs:35-36` | Ownership check | Fixed in Task 2 | Now uses `GetIdentifier()` for comparison; includes null-owner guard (FR-2 amendment) |
| `GetArticleFeedbackListHandler.cs:29` | Pass-through | Handler parameter pass | Forwards `request.RequestedBy` to repository |
| `GetArticleFeedbackListHandler.cs:49` | DTO emission | Projection | Projects `a.RequestedBy` into `ArticleFeedbackSummary`; now emits OID-based identifier |
| `GetArticleFeedbackListRequest.cs:9,38` | Request/Response DTOs | DTO properties | Carries the filter/response value; no logic at this level |
| `ArticlesController.cs:96` | API parameter binding | Controller pass-through | Binds `[FromQuery] requestedBy` query param to handler request |
| `GetArticleFeedbackListHandlerTests.cs:23,43` | Test fixture | Test setup/assertion | Uses opaque string `"alice"` in test setup and assertion; cosmetic, no logic change required |
| `SubmitArticleFeedbackHandlerTests.cs:31,90` | Test fixture | Test setup/assertion | Uses opaque strings like `"alice-oid-1111"` for RequestedBy in domain model setup; test logic remains valid |
| `GenerateArticleHandlerTests.cs:73,77,88,124` | Test assertion | Test verification | Asserts `RequestedBy` property matches expected OID or null; tests pass after Task 4 implementation |
| `frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts:12,20` | Adapter/Frontend | Value pass-through | Line 12: `requestedBy: params.userId` (passes userId param to API); Line 20: `userId: article.requestedBy` (extracts OID from response); no comparison logic |
| `frontend/src/api/generated/api-client.ts` | Generated code | Generated auto-build artifact | Auto-generated TypeScript client from OpenAPI spec; regenerates on `dotnet build`; no manual edit required |
| **Migration files** (multiple Designer.cs files) | Schema history | Migration metadata | EF Core migration snapshots documenting schema evolution; no logic or comparison |

## Key Findings

### No Unhandled Ownership Checks

Grep for ownership-style patterns (`RequestedBy == ` or `RequestedBy.Equals()`) returned only:
```
ArticleRepository.cs:76: query = query.Where(a => a.RequestedBy == requestedBy);
```

This is a **pass-through filter** — the comparison is between two database column values (one from the entity, one from the parameter). The parameter itself comes from the API query string, which is now OID-based after the frontend adapter change. No ownership logic at this site.

### Write Sites Verified

Both write sites have been updated to use `GetIdentifier()`:
- **GenerateArticleHandler.cs:46** — Creates articles with OID-based `RequestedBy`
- **SubmitArticleFeedbackHandler.cs:35-36** — Validates using OID comparison

### Test Coverage Intact

All test assertions use opaque string identifiers that remain valid under the OID-based semantics. The test logic (equality, null checks) continues to function without modification.

### Frontend Integration

The `useArticleFeedbackAdapter` correctly:
1. **Reads from params** — Receives `params.userId` (the current user's OID from frontend auth)
2. **Sends to API** — Passes as `requestedBy: params.userId` query parameter
3. **Reads from response** — Extracts `article.requestedBy` (OID) and maps to `userId` in the display model
4. **No comparison** — Adapter is purely a pass-through; no ownership checks

### Generated Code

The TypeScript API client is auto-generated from the OpenAPI spec and does not require manual review or changes.

## Conclusion

**Audit complete — no additional ownership-check sites found.**

All `RequestedBy` comparisons have been migrated to OID-based semantics. The system is ready for:
- Task 6+: Backfill implementation (for legacy articles with name-based or null RequestedBy)
- Task 12: Frontend UX update (displaying OID instead of name is a follow-up; tracked separately)
