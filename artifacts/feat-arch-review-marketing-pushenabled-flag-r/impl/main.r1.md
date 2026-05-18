All committed. Here is the output artifact:

---

# Implementation: Marketing PushEnabled Runtime Hot-Reload

## What was implemented

Converted all three Marketing write handlers (`CreateMarketingActionHandler`, `UpdateMarketingActionHandler`, `DeleteMarketingActionHandler`) from `IOptions<MarketingCalendarOptions>` to `IOptionsMonitor<MarketingCalendarOptions>`. The kill-switch (`PushEnabled`) is now read via `.CurrentValue` at handler execution time, so Azure App Configuration changes are honored without an application restart. A shared `TestOptionsMonitor<T>` test helper was extracted from `MarketingCategoryMapperTests` to a common location.

## Files created/modified

### New
- `backend/test/Anela.Heblo.Tests/Helpers/TestOptionsMonitor.cs` — shared IOptionsMonitor<T> test double with `.Set()` for runtime toggle testing

### Modified (handlers)
- `backend/src/.../CreateMarketingAction/CreateMarketingActionHandler.cs` — `IOptions` → `IOptionsMonitor`, `.Value` → `.CurrentValue`
- `backend/src/.../UpdateMarketingAction/UpdateMarketingActionHandler.cs` — same
- `backend/src/.../DeleteMarketingAction/DeleteMarketingActionHandler.cs` — same

### Modified (canonical tests)
- `backend/test/.../Application/Marketing/CreateMarketingActionHandlerTests.cs` — `TestOptionsMonitor` factory + 2 hot-reload tests
- `backend/test/.../Application/Marketing/UpdateMarketingActionHandlerTests.cs` — same
- `backend/test/.../Application/Marketing/DeleteMarketingActionHandlerTests.cs` — same

### Modified (legacy tests)
- `backend/test/.../Features/Marketing/CreateMarketingActionHandlerTests.cs` — updated constructor to use `TestOptionsMonitor`
- `backend/test/.../Features/Marketing/MarketingActionHandlerSyncTests.cs` — all three `BuildXxxHandler` methods updated
- `backend/test/.../Features/Marketing/Services/MarketingCategoryMapperTests.cs` — removed inline `TestOptionsMonitor<T>` nested class, added shared using

## Tests

- **96 marketing tests pass** — all original tests plus 6 new hot-reload tests (2 per handler: true→false and false→true)
- **3375 other unit tests pass** — no regressions
- 22 pre-existing Docker/Testcontainers failures in Photobank and KnowledgeBase (unrelated, require running Docker)

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Marketing"
# Expected: 96 passed
```

## Notes

Two duplicate test files exist alongside the canonical ones — they were updated to compile against the new `IOptionsMonitor<T>` constructor but were intentionally **not** deleted:
- `backend/test/Anela.Heblo.Tests/Features/Marketing/CreateMarketingActionHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs`

Recommend follow-up to consolidate or remove these duplicates.

`OutlookCalendarSyncService.cs` and `MarketingModule.cs` are **untouched** — out of scope per spec.

## PR Summary

Switch all three Marketing write handlers off `IOptions<T>` so the `PushEnabled` kill-switch responds to runtime configuration changes (e.g., Azure App Configuration) without an application restart. Previously, toggling `PushEnabled = false` during an incident had no effect on running handlers — a restart was required. Now all three handlers (`CreateMarketingActionHandler`, `UpdateMarketingActionHandler`, `DeleteMarketingActionHandler`) read `.CurrentValue.PushEnabled` at invocation time.

A shared `TestOptionsMonitor<T>` test helper was extracted from `MarketingCategoryMapperTests` (the existing hot-reload reference implementation) so all handler test suites can drive runtime toggle transitions. Six new unit tests cover the true→false and false→true transitions for each handler.

**Note:** Two duplicate Marketing handler test files in `Features/Marketing/` were updated to compile against the new constructor signature but were intentionally not deleted. Recommend a separate cleanup PR.

### Changes
- `backend/src/.../CreateMarketingAction/CreateMarketingActionHandler.cs` — `IOptions` → `IOptionsMonitor`, `.Value` → `.CurrentValue`
- `backend/src/.../UpdateMarketingAction/UpdateMarketingActionHandler.cs` — same
- `backend/src/.../DeleteMarketingAction/DeleteMarketingActionHandler.cs` — same
- `backend/test/.../Helpers/TestOptionsMonitor.cs` — new shared test double (promoted from `MarketingCategoryMapperTests`)
- `backend/test/.../Application/Marketing/*HandlerTests.cs` — use shared helper; add hot-reload tests
- `backend/test/.../Features/Marketing/*Tests.cs` — updated to compile; duplicates flagged for cleanup

## Status
DONE