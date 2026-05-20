# Implementation: Remove Redundant Try-Catch in GetOrganizationStructureHandler

## What was implemented
Removed the dead `try { ... } catch { log; throw; }` wrapper from `GetOrganizationStructureHandler.Handle` so failure paths log exactly once (from the controller). Added missing unit-test coverage verifying the new propagation behavior. The handler now matches every other handler in the codebase (happy-path-only, no self-logging on failure).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/GetOrganizationStructureHandler.cs` — removed try-catch block and intermediate `result` variable; `Handle` now logs informational entry then directly `return await`s the service call
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs` — new file; two xUnit tests using Moq + FluentAssertions

## Tests
`backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs`
- `Handle_ReturnsServiceResponse_WhenServiceSucceeds` — happy path: verifies the service response object is returned unchanged (BeSameAs)
- `Handle_PropagatesException_WhenServiceThrows` — exception path: verifies the exception propagates unchanged (BeSameAs) and that the handler emits NO LogError (Times.Never)

All 3623 existing tests in the test suite continue to pass.

## How to verify
```bash
# Run only the new handler tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetOrganizationStructureHandlerTests" --nologo

# Run full suite
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo
```

Both handler tests pass. No regressions.

## Notes
- The controller's catch block (`OrgChartController.cs` lines 40–53) is unchanged — it remains the single authoritative point for logging failures and returning 500.
- Per the arch-review amendment: the controller log message and exception attachment are verbatim identical to what the handler was logging, so NFR-4 (observability richness) is satisfied without any controller change.
- Pre-existing concern noted in arch review: `OrgChartController.cs:51` returns `ex.Message` in the 500 body. This is out of scope for this change but flagged for a future arch review.
- Commits: `16454b0c` (test file, RED phase) and `048d3ecd` (handler edit, GREEN phase).

## PR Summary
Removes the redundant try-catch wrapper from `GetOrganizationStructureHandler.Handle` that logged and rethrew without adding any behavior. The controller already owns failure logging for this endpoint, so the handler-level catch was producing a duplicate ERROR log entry on every failure. After this change each org-chart failure produces exactly one error log (from the controller).

Also adds the first unit tests for this handler — a happy-path test and an exception-propagation test that explicitly asserts the handler does not emit its own LogError, enforcing the single-responsibility boundary.

### Changes
- `backend/src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/GetOrganizationStructureHandler.cs` — removed try-catch; `Handle` is now a two-liner (log + return await)
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs` — new file; two tests covering happy path and exception propagation

## Status
DONE
