# Implementation: inject-timeprovider-manufacture-handlers

## What was implemented
Injected `TimeProvider` into the three Manufacture module handlers that bypassed the module's established time-abstraction pattern, replacing direct `DateTime.UtcNow` / `DateTime.Now` calls with `_timeProvider.GetUtcNow().DateTime`, matching the existing pattern in `UpdateManufactureOrderStatusHandler`. `GetSemiproductRecipePdfHandler` also gets a behavior fix: `PrintedAt` now uses UTC instead of the server's local time.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs` — added `TimeProvider` constructor injection; `GeneratedAt` now uses `_timeProvider.GetUtcNow().DateTime`.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ResolveManualAction/ResolveManualActionHandler.cs` — added `TimeProvider` constructor injection; `ErpDiscardResidueDocumentNumberDate` and the new note's `CreatedAt` now use `_timeProvider.GetUtcNow().DateTime`.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetSemiproductRecipePdf/GetSemiproductRecipePdfHandler.cs` — added `TimeProvider` constructor injection; `PrintedAt` now uses `_timeProvider.GetUtcNow().DateTime` (was `DateTime.Now`, a local-time bug).
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs` — constructor call updated to pass `TimeProvider.System`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ResolveManualActionHandlerTests.cs` — constructor call updated to pass `TimeProvider.System`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetSemiproductRecipePdfHandlerTests.cs` — constructor call updated to pass `TimeProvider.System`.

No production call site manually constructs these handlers outside of DI (verified by grep) — `TimeProvider` is already registered as a singleton in `ServiceCollectionExtensions.cs`, so no DI registration change was needed.

## Tests
The three existing test files above were updated only to satisfy the new constructor parameter (`TimeProvider.System`); no test behavior or assertions were weakened.

## How to verify
```bash
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Manufacture"
grep -rn "DateTime.UtcNow\|DateTime.Now" backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ResolveManualAction backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetSemiproductRecipePdf
```
Build succeeds (0 errors). `Anela.Heblo.Tests.dll` (the project containing the three touched handler test files): 755/755 passed. `dotnet format Anela.Heblo.sln --verify-no-changes` reports no diffs.

## Notes
`dotnet test --filter "FullyQualifiedName~Manufacture"` also picks up `Anela.Heblo.Adapters.Flexi.Tests` (a different project/layer, `FlexiManufactureClient*` integration tests), where 7 tests fail with `FlexiIntegrationTestFixture` throwing `ArgumentNullException` from `AddFlexiBee` DI registration. This is a pre-existing environment/config issue (missing FlexiBee integration credentials in this sandbox) entirely unrelated to this change — those tests live in the Adapters.Flexi layer and have no dependency on the three Manufacture Application-layer handlers touched here.

## PR Summary
Three Manufacture module handlers (`GetManufactureProtocolHandler`, `ResolveManualActionHandler`, `GetSemiproductRecipePdfHandler`) called `DateTime.UtcNow`/`DateTime.Now` directly instead of using the module's injected `TimeProvider` abstraction, unlike every other handler in the module. This made their timestamps untestable with a deterministic clock, and `GetSemiproductRecipePdfHandler` had a real bug: `DateTime.Now` returns local server time, not UTC, so `PrintedAt` could be off by an hour or more relative to every other UTC timestamp in the system if the server ever runs in a non-UTC timezone.

All three handlers now inject `TimeProvider` via their constructor and call `_timeProvider.GetUtcNow().DateTime`, matching the existing pattern already used by `UpdateManufactureOrderStatusHandler` and others in the module. `TimeProvider` was already registered as a DI singleton, so no registration changes were needed. The three existing unit test files for these handlers were updated to pass `TimeProvider.System` to the new constructor parameter.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ResolveManualAction/ResolveManualActionHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetSemiproductRecipePdf/GetSemiproductRecipePdfHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ResolveManualActionHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetSemiproductRecipePdfHandlerTests.cs`

## Status
DONE
