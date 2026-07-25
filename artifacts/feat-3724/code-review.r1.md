## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs:150` — The newly-thrown `GraphServiceException` is not caught by the preceding `catch (MsalException)` / `catch (ODataError)` / `catch (UnauthorizedAccessException)` blocks, so it falls through to the trailing `catch (Exception ex)` block, which logs a second, misleadingly-labeled entry ("Unexpected error fetching group members for group {GroupId}") before rethrowing via `throw;`. The exception type/message/inner-exception are preserved correctly (functionally harmless), but every non-success Graph response now produces two `LogError` calls for the same event, the second one mischaracterizing an already-diagnosed HTTP failure as "unexpected." Consider adding `catch (GraphServiceException) { throw; }` above the generic catch (mirroring how `GraphServiceAuthException`-shaped errors are already isolated) to keep logging single-sourced.

### Notes
- Verified `GetGroupMembersHandler` and `GraphArticleUserResolver` already have `catch (GraphServiceException)` blocks matching the documented `IGraphService` contract — no caller-side changes needed or made, consistent with the spec's "Out of Scope" section.
- Verified no other test in `GraphServiceTests.cs`, `GetGroupMembersHandlerTests.cs`, or `GetGroupMembersValidationPipelineTests.cs` relies on the old empty-list behavior; handler tests mock `IGraphService` directly and already exercise the `GraphServiceException` → `ExternalServiceError` path.
- `GetAppRoleMembersAsync`'s call into `GetGroupMembersAsync` (for nested-group expansion) is inside its own top-level `catch (Exception ex)`, so a newly-thrown `GraphServiceException` there is still swallowed into an empty-list result — this matches NFR-1's explicit acceptance of that pre-existing, out-of-scope behavior.
- New test `GetGroupMembersAsync_GraphReturnsTooManyRequests_ThrowsGraphServiceException` (429) correctly complements the 403 case; both assert status code, group id, and non-null inner exception, matching the implementation's message format exactly.
