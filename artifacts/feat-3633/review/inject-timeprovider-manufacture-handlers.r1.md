# Code Review: Inject TimeProvider into Manufacture handlers

## Summary
The implementation matches the spec exactly: all three handlers (`GetManufactureProtocolHandler`, `ResolveManualActionHandler`, `GetSemiproductRecipePdfHandler`) now inject `TimeProvider` via the constructor and use `_timeProvider.GetUtcNow().DateTime` in place of every previous `DateTime.UtcNow`/`DateTime.Now` call, and the three corresponding test files were updated to pass `TimeProvider.System` at their construction call sites. I independently verified the diff (not just the implementation summary), rebuilt the solution, and ran the targeted test suite.

## Review Result: PASS

### task: inject-timeprovider-manufacture-handlers
**Status:** PASS

## Docs to Update
(None — this is an internal implementation-only refactor with no public API, DTO, or contract changes.)

## Overall Notes
Verification performed directly against `git show HEAD -- backend/`:
- `GetManufactureProtocolHandler.cs`: field + constructor param added exactly as specified; line 85 `GeneratedAt = _timeProvider.GetUtcNow().DateTime,`; no other lines touched.
- `ResolveManualActionHandler.cs`: field + constructor param added; both `order.ErpDiscardResidueDocumentNumberDate` (line 54→57) and `CreatedAt` (line 66→69) replaced; no other lines touched.
- `GetSemiproductRecipePdfHandler.cs`: field + constructor param added; `PrintedAt` now uses `_timeProvider.GetUtcNow().DateTime` instead of `DateTime.Now` (the intended FR-3 UTC bug fix); no other lines touched.
- All three `TimeProvider` parameters are appended as the **last** constructor parameter, matching the reference pattern.
- The three test files (`GetManufactureProtocolHandlerTests.cs`, `ResolveManualActionHandlerTests.cs`, `GetSemiproductRecipePdfHandlerTests.cs`) were updated to pass `TimeProvider.System` as the new last argument at their single construction call site each — matching `UpdateManufactureOrderStatusHandlerTests.cs`'s convention.
- Repo-wide search (`grep -rn "new GetManufactureProtocolHandler(\|new ResolveManualActionHandler(\|new GetSemiproductRecipePdfHandler("`) found matches only in the three test files — no other manual production construction site exists; production resolution goes through MediatR/DI.
- `TimeProvider` is already registered as a DI singleton (`services.AddSingleton(TimeProvider.System);` in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:131`), so no DI registration change was required, consistent with the spec's stated assumption.
- Targeted grep for stray `DateTime.UtcNow`/`DateTime.Now` in the three handler files returned zero matches.
- `dotnet build Anela.Heblo.sln` succeeds with 0 errors (249 pre-existing warnings in unrelated files, none introduced by this change).
- `dotnet test --filter "FullyQualifiedName~Manufacture"` on `Anela.Heblo.Tests.dll`: 755/755 passed, matching the implementation summary's claim.
- The implementation summary's note about 7 unrelated `Anela.Heblo.Adapters.Flexi.Tests` failures (pre-existing `FlexiIntegrationTestFixture`/`AddFlexiBee` DI/config issue in this sandbox, unrelated to the Application-layer handlers touched) is plausible and out of scope for this change; not re-verified since it doesn't touch the code under review.

All acceptance criteria from the task spec are satisfied. No functional, architectural, or test-coverage issues found.
