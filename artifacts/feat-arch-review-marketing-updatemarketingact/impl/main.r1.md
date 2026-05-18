All tasks complete. Writing the output artifact:

---

# Implementation: Extract Shared MarketingFolderLinkRequest DTO

## What was implemented

Replaced the cross-contract nested type `CreateMarketingActionRequest.CreateFolderLinkRequest` with a standalone `MarketingFolderLinkRequest` class, consumed by both Create and Update marketing-action request contracts. The wire format is byte-for-byte unchanged. The OpenAPI/NSwag TypeScript client was regenerated and now exposes `MarketingFolderLinkRequest` under the new name.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MarketingFolderLinkRequest.cs` — new shared DTO class (`FolderKey`, `FolderType`; block-scoped namespace; plain class per project rule)
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/CreateMarketingActionRequest.cs` — `FolderLinks` retyped to `List<MarketingFolderLinkRequest>?`; nested `CreateFolderLinkRequest` class deleted
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/UpdateMarketingActionRequest.cs` — `FolderLinks` retyped to `List<MarketingFolderLinkRequest>?`
- `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs` — new `Handle_PersistsFolderLinks_WhenProvided` test added (NFR-3 coverage); type updated to `MarketingFolderLinkRequest`
- `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs` — existing `Handle_UpdatesProductsAndFolderLinks_WhenProvided` test updated to `MarketingFolderLinkRequest`
- `frontend/src/api/generated/api-client.ts` — regenerated via `dotnet msbuild -t:GenerateFrontendClientManual`; class renamed from `CreateFolderLinkRequest` to `MarketingFolderLinkRequest`

## Tests

- All 27 marketing handler tests pass (unit tests in `Anela.Heblo.Tests.Application.Marketing`)
- New `Handle_PersistsFolderLinks_WhenProvided` test covers the Create handler's `FolderLinks` path (previously uncovered)
- Existing `Handle_UpdatesProductsAndFolderLinks_WhenProvided` continues to cover the Update handler path
- 22 pre-existing `KnowledgeBase.Integration` failures (require live DB connection, unrelated to this change)

## How to verify

```bash
# Backend acceptance gates
dotnet build                                           # 0 errors
dotnet test --filter "FullyQualifiedName~Application.Marketing" --no-build  # 27/27 pass
grep -rn "CreateFolderLinkRequest" backend/ frontend/src/  # zero output

# Frontend
cd frontend && npm run build    # Compiled successfully
```

## Notes

- `npm run build` alone does not regenerate the TypeScript client — it requires `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual` (or the Debug dotnet build). The prebuild script invokes this but requires msbuild, not the plain `react-scripts build`.
- Frontend lint has 106 pre-existing errors across testing-library conventions in unrelated test files (marketing component tests included, but the errors there are node-access / waitFor patterns, not type errors). None were introduced by this refactoring.
- Handlers (`CreateMarketingActionHandler.cs`, `UpdateMarketingActionHandler.cs`) were intentionally left untouched — they reference only `request.FolderLinks`, `link.FolderKey`, and `link.FolderType`, so no type name appears in handler code.

## PR Summary

Eliminates an Interface Segregation violation in the marketing module where `UpdateMarketingActionRequest` depended on a nested type owned by `CreateMarketingActionRequest`. A new `MarketingFolderLinkRequest` sibling class (same shape, same namespace) is now the single shared contract for folder-link data across both use cases. Wire format is unchanged; the TypeScript client regeneration renames the component transparently.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MarketingFolderLinkRequest.cs` — new shared DTO
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/CreateMarketingActionRequest.cs` — retype + delete nested class
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/UpdateMarketingActionRequest.cs` — retype
- `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs` — new baseline test + type update
- `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs` — type update
- `frontend/src/api/generated/api-client.ts` — regenerated

## Status
DONE_WITH_CONCERNS

**Concerns:**
1. `npm run lint` reports 106 pre-existing errors (testing-library conventions) that were present before this change. NFR-2 states lint should pass — it doesn't, but that's a pre-existing state, not a regression introduced here.
2. The `npm run build` prebuild step invokes `dotnet msbuild -t:GenerateFrontendClientManual` for client generation. If `dotnet` is not on PATH at npm build time (CI or fresh checkouts), the client won't regenerate automatically. Manual step documented in Notes above.