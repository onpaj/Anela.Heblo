All 13 tasks passed review. Writing the implementation summary.

---

# Implementation: Centralize MarketingAction scalar field updates via domain method

## What was implemented

Added a parameterized constructor and `UpdateDetails` domain method to `MarketingAction`, both delegating to private static `NormalizeTitle`/`NormalizeDescription` helpers. Refactored all three mutation paths (Create handler, Update handler, Outlook import mapper) to use these surfaces. Tightened 11 scalar property setters to `private set`. Fixed `HasChanges` to compare normalized values (preventing a sync-loop regression). Introduced `MarketingActionTestBuilder` to absorb the constructor-ripple across test files.

## Files created/modified

**Production code:**
- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — added parameterized ctor + `UpdateDetails` + private normalizers; tightened 11 setters to `private set`; narrowed parameterless ctor to `private`
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs` — uses new constructor
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs` — uses `UpdateDetails`
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs` — `BuildAction` uses constructor, `ApplyChanges` uses `UpdateDetails`, `HasChanges` normalizes before comparison, SA-2 null-guards on `currentUser.Id`

**Test code — new files:**
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionTestBuilder.cs` — fluent builder; single place to update when entity construction surface changes
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionUpdateDetailsTests.cs` — 7 unit tests for FR-6
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionConstructorTests.cs` — 6 unit tests for SA-3

**Test code — modified (migrated to builder):**
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionAssociateWithProductTests.cs`
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionSyncTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/Marketing/MarketingActionDtoTests.cs`
- `backend/test/Anela.Heblo.Tests/Marketing/OutlookCalendarSyncServiceTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Marketing/CreateMarketingActionHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Marketing/ImportFromOutlookHandlerTests.cs` — migrated + 3 new tests (SA-1 regression, FR-7 new event trim, FR-7 update trim)
- `backend/test/Anela.Heblo.Tests/Features/Marketing/UpdateMarketingActionHandlerTests.cs`, `DeleteMarketingActionHandlerTests.cs`, `GetMarketingCalendarHandlerTests.cs`, `OutlookCalendarSyncServiceTokenTests.cs` — migrated to builder

## Tests

| Test file | Coverage |
|-----------|----------|
| `MarketingActionUpdateDetailsTests.cs` | 7 tests: title trim, null→empty string, null description preserved, description trim, ModifiedByUsername default, ModifiedAt assignment, scalar fields |
| `MarketingActionConstructorTests.cs` | 6 tests: title trim, description trim, null description, username default, timestamps, scalars |
| `ImportFromOutlookHandlerTests.cs` (new tests) | SA-1 regression (whitespace re-import → Skipped), FR-7 new event trim, FR-7 update event trim |

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.sln --nologo
dotnet test Anela.Heblo.sln --nologo --filter "FullyQualifiedName~Marketing"
grep -rn "action\.\(Title\|Description\|ActionType\|StartDate\|EndDate\|ModifiedAt\|ModifiedByUserId\|ModifiedByUsername\|CreatedAt\|CreatedByUserId\|CreatedByUsername\)\s*=" src/Anela.Heblo.Application
# Expected: zero matches
```

## Notes

- Pre-existing Docker/Testcontainer integration test failures (36 tests) are unrelated to this change — they require a running PostgreSQL container
- `dotnet format` produced no changes — code was already formatted
- Four additional test files beyond the Task 1 plan needed builder migration (they used the parameterless object-initializer pattern); fixed in Task 12
- The redundant `.Trim()` on `ParseDescription` result in `HasChanges` is a no-op (description is already trimmed by `StripHtml`) — noted as LOW severity, not blocking

## PR Summary

This change eliminates whitespace inconsistency in `MarketingAction.Title` by centralizing normalization in a domain method, replacing scattered direct property mutations across three call sites. Titles imported from Outlook now match titles set via the API, fixing exact-match search and deduplication bugs caused by surrounding whitespace.

The implementation adds `MarketingAction(...)` constructor and `UpdateDetails(...)` method (both delegating to `NormalizeTitle`/`NormalizeDescription`), tightens 11 scalar property setters to `private set` to enforce the encapsulation boundary, and fixes `HasChanges` in the Outlook import mapper so re-importing whitespace-titled events is correctly identified as unchanged rather than triggering a no-op write loop.

### Changes
- `MarketingAction.cs` — parameterized ctor + `UpdateDetails` + private normalizers + `private set` on 11 properties
- `CreateMarketingActionHandler.cs` — constructor call replaces object initializer
- `UpdateMarketingActionHandler.cs` — `UpdateDetails` call replaces 8 direct property assignments
- `OutlookEventImportMapper.cs` — `BuildAction` uses ctor, `ApplyChanges` uses `UpdateDetails`, `HasChanges` normalizes before comparison, SA-2 null-guards added
- `MarketingActionTestBuilder.cs` (new) — fluent builder centralizing test fixture construction
- `MarketingActionUpdateDetailsTests.cs` + `MarketingActionConstructorTests.cs` (new) — unit tests for normalization
- 11 test files — migrated from object-initializer construction to `MarketingActionTestBuilder`
- `ImportFromOutlookHandlerTests.cs` — 3 new tests: sync-loop regression guard + FR-7 trim on create + FR-7 trim on update

## Status

DONE