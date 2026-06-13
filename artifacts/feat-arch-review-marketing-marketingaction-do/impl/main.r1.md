---

# Implementation: MarketingAction Timestamp Parameter Consistency

## What was implemented

Refactored three domain methods on `MarketingAction` to accept a `DateTime utcNow` parameter instead of calling `DateTime.UtcNow` directly, aligning them with the existing convention used by `UpdateDetails()` and the constructor. Extended `IMarketingActionRepository.DeleteSoftAsync` to accept the same parameter so the handler-captured timestamp propagates cleanly through the persistence layer.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — `AssociateWithProduct`, `LinkToFolder`, `SoftDelete` signatures updated; no `DateTime.UtcNow` reads remain
- `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs` — `DeleteSoftAsync` extended with `DateTime utcNow` parameter
- `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs` — `DeleteSoftAsync` impl forwards `utcNow` to `entity.SoftDelete`
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs` — `AssociateWithProduct` and `LinkToFolder` calls pass `now`
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs` — same
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs` — captures `var now = DateTime.UtcNow;` after auth check; passes it to `DeleteSoftAsync`
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionAssociateWithProductTests.cs` — all calls updated; `BeCloseTo` replaced with exact `.Be()`; new exact-equality test added
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionLinkToFolderTests.cs` — **new file**; 4 tests covering timestamp exact-equality, no-op on duplicate, trimming, null/empty/whitespace validation
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionSoftDeleteTests.cs` — **new file**; 3 tests: exact-equality for `DeletedAt`/`ModifiedAt`, identity regression (`DeletedAt == ModifiedAt`), audit field population
- `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs` — all `DeleteSoftAsync` mock/verify signatures updated; new `Handle_PassesCapturedNow_ToDeleteSoftAsync` test added
- `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs` — 3 `DeleteSoftAsync` references updated with `DateTime` parameter

## Tests

- **MarketingActionAssociateWithProductTests** (9 tests) — exact timestamp equality; all pre-existing behavior tests updated
- **MarketingActionLinkToFolderTests** (4 tests, new file) — full coverage of new 3-arg signature
- **MarketingActionSoftDeleteTests** (3 tests, new file) — exact equality, identity regression, audit fields
- **DeleteMarketingActionHandlerTests** (11 tests) — updated mocks + new captured-now forwarding test
- **MarketingActionHandlerSyncTests** (8 tests) — updated mocks

## How to verify

```bash
cd backend
dotnet build                                    # 0 errors
dotnet format --verify-no-changes               # exit 0
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Marketing"
```

FR-5 grep gates (both must return zero matches):
```
grep -n "DateTime\.\(Utc\)\?Now" backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs
grep -n "DateTime\.\(Utc\)\?Now" backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs
```

## Notes

- The architecture review correctly identified that `SoftDelete` is not called directly from `DeleteMarketingActionHandler` — it goes through `IMarketingActionRepository.DeleteSoftAsync`. The repository interface was extended accordingly (Decision 1 from the arch review: Option A).
- The `DeleteMarketingActionHandler` did not previously capture `var now = DateTime.UtcNow;` (the brief's claim was incorrect). The handler now captures it immediately after the auth check, matching the sibling handler pattern.
- 38 pre-existing test failures in the full suite are Docker/Testcontainers integration tests unrelated to this change; all 37 Marketing tests pass.
- `JournalEntry` files untouched (verified via `git diff --stat main`).

## PR Summary

Eliminated three direct `DateTime.UtcNow` reads inside `MarketingAction.cs` (`AssociateWithProduct`, `LinkToFolder`, `SoftDelete`) by adding a `DateTime utcNow` parameter to each method, matching the convention already used by the constructor and `UpdateDetails`. Extended `IMarketingActionRepository.DeleteSoftAsync` with the same parameter so the handler-captured timestamp flows cleanly from handler → repository → domain without re-reading the clock at each layer. This makes all three methods deterministically testable — tests now assert exact timestamp equality instead of fragile tolerance windows, and the `DeletedAt`/`ModifiedAt` fields on soft-delete are now guaranteed identical (previously two separate clock reads could produce different values).

### Changes
- `MarketingAction.cs` — `AssociateWithProduct`, `LinkToFolder`, `SoftDelete` accept `DateTime utcNow`; no `DateTime.UtcNow` reads remain
- `IMarketingActionRepository.cs` — `DeleteSoftAsync` extended with `DateTime utcNow`
- `MarketingActionRepository.cs` — forwards `utcNow` to `entity.SoftDelete`
- `DeleteMarketingActionHandler.cs` — captures `var now = DateTime.UtcNow;` once after auth check
- `CreateMarketingActionHandler.cs`, `UpdateMarketingActionHandler.cs` — pass `now` to `AssociateWithProduct` and `LinkToFolder`
- `MarketingActionAssociateWithProductTests.cs` — all calls updated; exact equality assertions
- `MarketingActionLinkToFolderTests.cs` — new file, 4 tests
- `MarketingActionSoftDeleteTests.cs` — new file, 3 tests including `DeletedAt == ModifiedAt` regression
- `DeleteMarketingActionHandlerTests.cs` — updated mocks + new captured-now forwarding test
- `MarketingActionHandlerSyncTests.cs` — updated mocks

## Status
DONE