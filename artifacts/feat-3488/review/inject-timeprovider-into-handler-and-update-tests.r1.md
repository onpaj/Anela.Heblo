# Code Review: Inject TimeProvider into GetInvoiceImportStatisticsHandler

## Summary
The implementation matches the task spec exactly: `GetInvoiceImportStatisticsHandler` now takes an injected `TimeProvider` and uses `_timeProvider.GetUtcNow().Date` instead of `DateTime.UtcNow.Date`, and all 4 handler construction sites in the test file were updated to pass a mocked `TimeProvider` returning a fixed date. Verified the actual file contents against the spec's prescribed diff (byte-for-byte match) and independently ran a full solution build and the targeted test filter.

## Review Result: PASS

### task: inject-timeprovider-into-handler-and-update-tests
**Status:** PASS

## Verification performed
- Read `GetInvoiceImportStatisticsHandler.cs`: contains the `_timeProvider` field, the new constructor parameter, and `var endDate = _timeProvider.GetUtcNow().Date;` — matches the spec's prescribed replacement exactly, with no unrelated changes.
- Read `GetInvoiceImportStatisticsHandlerTests.cs`: `Mock<TimeProvider>` is stubbed to return a fixed `DateTime(2025, 10, 14, 10, 0, 0, DateTimeKind.Utc)`; all 4 constructor call sites (main `_handler` field init plus 3 ad-hoc handlers in individual tests) pass `_timeProviderMock.Object`; date-dependent assertions (`Handle_ShouldUseConfigurableDefaultDaysBack`, `Handle_ShouldUseDefaultValuesWhenOptionsAreParameterless`) now derive expected dates from `_fixedDateTime` rather than wall-clock time.
- `grep` confirms no remaining `DateTime.UtcNow`/`DateTime.Now` in either file.
- Ran `dotnet build Anela.Heblo.sln`: succeeded, 0 errors (250 pre-existing warnings unrelated to this change).
- Ran `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetInvoiceImportStatisticsHandlerTests"`: 6/6 passed.
- Confirmed `TimeProvider.System` is registered as singleton in `ServiceCollectionExtensions.cs:131`, so no DI changes were needed, consistent with the impl's claim.
- Confirmed the change was committed (`faf363f Inject TimeProvider into GetInvoiceImportStatisticsHandler`) with only the two target files touched (plus the pipeline's own `state.json`, unrelated to this task).
- The impl summary's note about 64 pre-existing Testcontainers/Docker-related failures in the full suite is a known sandbox limitation unrelated to this change and is not a basis for REVISION_NEEDED, per review instructions.

## Overall Notes
Implementation is a clean, minimal, surgical change that exactly follows the prescribed diff and the established pattern from `GetBankStatementImportStatisticsHandler`. No documentation updates are needed — this is an internal determinism fix with no change to public API behavior, contracts, or operational concerns.
