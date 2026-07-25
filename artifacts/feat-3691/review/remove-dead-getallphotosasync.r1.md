# Code Review: Photobank – Remove Dead GetAllPhotosAsync

## Summary
The implementation correctly removes the now-unused `GetAllPhotosAsync` method from the interface, repository implementation, and tests. The removal is surgical, affects no other code paths, and maintains proper code spacing. All acceptance criteria from FR-4 are satisfied.

## Review Result: PASS

### task: remove-dead-getallphotosasync
**Status:** PASS

**Verification:**
- Interface declaration removed from `IPhotobankRepository.cs` with proper spacing maintained between adjacent methods.
- Implementation removed from `PhotobankRepository.cs`, correctly preserving single blank line between `GetLocatorAsync` and `GetPhotoBySharePointFileIdAsync`.
- Test method `GetAllPhotosAsync_returnsAllPhotos` removed from `PhotobankRepositoryReapplyPrimitivesTests.cs`; equivalent coverage exists via `GetPhotoRuleCandidatesPageAsync_*` tests from the prior task.
- Grep confirms zero remaining references to `GetAllPhotosAsync` across all backend/src and backend/test C# files.
- `dotnet build Anela.Heblo.sln` succeeds (0 errors, pre-existing unrelated warnings only).
- Photobank test suite: 187/190 pass. Three failures (`PhotobankRepositoryGetTagsSqlShapeTests`) are pre-existing, Docker/Testcontainers-dependent, and unrelated to this change:
  - Test file unchanged by this feature (last modified commit b2c208b, prior to feature branch).
  - Test class uses `PostgresSharedContainerFixture` (Testcontainers.PostgreSql integration).
  - Failures are environment infrastructure (Docker daemon unavailable in sandbox), not code-level regressions.
- Code formatting verified clean (`dotnet format --verify-no-changes` passes).

## Overall Notes
This is a clean, spec-compliant deletion task with no edge cases. The removal completes FR-4 as stated and leaves no orphaned references. The commit message accurately reflects the change scope.
