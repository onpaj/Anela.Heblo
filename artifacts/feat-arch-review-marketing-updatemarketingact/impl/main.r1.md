---

# Implementation: Encapsulate Collection Replacement in MarketingAction Domain Entity

## What was implemented

Added `ReplaceProductAssociations` and `ReplaceFolderLinks` to the `MarketingAction` aggregate root, then refactored `UpdateMarketingActionHandler` to delegate to them instead of directly mutating EF Core navigation collections.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — two new domain methods: `ReplaceProductAssociations(IEnumerable<string>?, DateTime)` and `ReplaceFolderLinks(IEnumerable<(string, MarketingFolderType)>?, DateTime)`, each with XML docs covering null→empty, normalization, dedup key, and the folderLink/composite-key asymmetry
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs` — replaced lines 95–111 (17-line Clear+loop block) with two delegated calls
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceProductAssociationsTests.cs` — 9 unit tests: empty, null, normalize+dedup, throw-on-invalid (theory×3), delta, utcNow, MarketingActionId
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceFolderLinksTests.cs` — 11 unit tests: empty, null, trim, composite-key dedup, same-key-diff-type, throw-on-invalid (theory×3), delta, utcNow, MarketingActionId
- `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs` — added `BuildExistingActionWithCollections()` helper + 2 new tests locking in clear-on-null and delta-replace contracts

## Tests

- `MarketingActionReplaceProductAssociationsTests` — 9 tests, all pass
- `MarketingActionReplaceFolderLinksTests` — 11 tests, all pass
- `UpdateMarketingActionHandlerTests` — 13 tests total (11 existing + 2 new), all pass
- 178 Marketing tests total, all green; full suite 4802 pass, 38 pre-existing Docker integration failures (unrelated)

## How to verify

```bash
cd backend
dotnet build --no-restore
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Marketing" --no-restore
# Verify no direct collection mutations remain in Application layer:
grep -rn "ProductAssociations\.Clear\|FolderLinks\.Clear" src/Anela.Heblo.Application/ || echo "No direct mutations"
```

## Notes

- Dedup key for `ReplaceFolderLinks` is composite `(folderKey, folderType)`, intentionally stricter than `LinkToFolder` (which dedupes by `folderKey` alone) — documented in XML doc comment
- Both new methods validate per-entry null/whitespace and throw `ArgumentException` (matching existing single-add methods)
- `null` sequence is treated as empty, consistent with spec FR-1/FR-2
- No new NuGet packages, no EF config changes, no migrations needed
- All commits are on `feat-arch-review-marketing-updatemarketingact` branch

## PR Summary

Encapsulates collection replacement in the `MarketingAction` aggregate by adding `ReplaceProductAssociations` and `ReplaceFolderLinks` domain methods, and refactoring `UpdateMarketingActionHandler` to delegate to them instead of directly calling `.Clear()` on EF navigation properties. This removes a persistence-layer concern from the Application layer and gives the domain entity full control over its invariants (normalization, deduplication, validation) for both add and replace operations.

### Changes
- `MarketingAction.cs` — two new replace methods with full XML documentation
- `UpdateMarketingActionHandler.cs` — 17-line Clear+loop block replaced with 3-line delegation
- `MarketingActionReplaceProductAssociationsTests.cs` — new file, 9 domain unit tests
- `MarketingActionReplaceFolderLinksTests.cs` — new file, 11 domain unit tests
- `UpdateMarketingActionHandlerTests.cs` — 2 new handler tests locking in clear-on-null and delta-replace contracts

## Status
DONE