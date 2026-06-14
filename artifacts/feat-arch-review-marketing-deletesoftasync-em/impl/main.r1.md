All 5 tasks complete. Here is the output summary:

---

# Implementation: Remove embedded SaveChangesAsync from IMarketingActionRepository.DeleteSoftAsync

## What was implemented

Refactored the Marketing module's soft-delete path so `DeleteMarketingActionHandler` owns the persistence sequence. Removed `DeleteSoftAsync` from `IMarketingActionRepository` and inlined `entity.SoftDelete() → UpdateAsync → SaveChangesAsync` directly in the handler, eliminating a hidden mid-handler commit and a redundant second DB load. The implementation matches the precedent set by `DeleteJournalEntryHandler`.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs` — `DeleteSoftAsync` declaration removed
- `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs` — `DeleteSoftAsync` implementation removed
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs` — `DeleteSoftAsync` call replaced with `action.SoftDelete(userId, username)` → `UpdateAsync` → `SaveChangesAsync` inside existing try/catch
- `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs` — all `DeleteSoftAsync` mocks/verifications switched to `UpdateAsync` + `SaveChangesAsync`; new regression test `Handle_LoadsEntityExactlyOnce_PerDeleteRequest` added
- `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs` — 3 `DeleteSoftAsync` references replaced with `UpdateAsync` + `SaveChangesAsync`

## Tests

- `DeleteMarketingActionHandlerTests` — 11 tests, all pass. Includes new `Handle_LoadsEntityExactlyOnce_PerDeleteRequest` regression guard
- `MarketingActionHandlerSyncTests` — delete handler tests updated and passing
- 157 Marketing tests total pass; 38 Docker-dependent integration test failures are pre-existing and unrelated

## How to verify

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~Marketing"
grep -rn "DeleteSoftAsync" backend/src backend/test  # expected: zero output
```

## Notes

- `MarketingAction.SoftDelete` is 2-arg (captures `DateTime.UtcNow` internally) — the spec example showing a third `now` arg was a spec typo per arch-review §Spec Amendments; the 2-arg form was used throughout
- `action.SoftDelete(...)` is placed **outside** the try block (in-memory operation); only `UpdateAsync` and `SaveChangesAsync` are inside the try/catch
- `UpdateAsync` call is retained even though EF already tracks the entity — matches `UpdateMarketingActionHandler` and `DeleteJournalEntryHandler` pattern
- 38 pre-existing test failures are Testcontainers integration tests requiring Docker; unrelated to this change

## PR Summary

Removed the hidden `SaveChangesAsync` from `IMarketingActionRepository.DeleteSoftAsync` and moved the entire soft-delete sequence into `DeleteMarketingActionHandler`, aligning the Marketing delete path with the caller-controlled persistence pattern already used by the Create, Update, and Journal delete handlers.

The previous implementation had two problems: a hidden mid-handler commit (the repository called `SaveChangesAsync` internally, invisible to callers) and a redundant second DB load (the repository re-fetched the entity the handler had already loaded for the Outlook event lookup). The new sequence reuses the entity already in memory: `action.SoftDelete(userId, name) → UpdateAsync(action) → SaveChangesAsync()`, wrapped in the existing try/catch. DB round-trips for the delete path drop from 3 to 2 (one read + one commit).

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs` — removed `DeleteSoftAsync` declaration
- `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs` — removed `DeleteSoftAsync` implementation
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs` — inlined soft-delete sequence
- `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs` — retargeted mocks/verifications; added entity-loaded-once regression test
- `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs` — retargeted 3 `DeleteSoftAsync` references

## Status
DONE