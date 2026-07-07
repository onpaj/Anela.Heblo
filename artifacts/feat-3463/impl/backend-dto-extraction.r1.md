# Implementation: backend-dto-extraction

## What was implemented
Removed the `IsBelowThreshold` property from the Domain type `DailyInvoiceCount`
(`backend/src/Anela.Heblo.Domain/Features/Analytics/DailyInvoiceCount.cs`), which was
being set by application-layer code — a Clean Architecture violation. Added a new
Application-layer `DailyInvoiceCountDto` class in `Contracts/`, and updated
`GetInvoiceImportStatisticsHandler` to project `DailyInvoiceCount` → `DailyInvoiceCountDto`
via `Select`, computing `IsBelowThreshold = c.Count < minimumThreshold` at projection time
instead of mutating the Domain object. `GetInvoiceImportStatisticsResponse.Data` is now
`List<DailyInvoiceCountDto>`. Removed the now-dead `IsBelowThreshold = false` initializers
from `InvoiceImportStatisticsSourceAdapter` and the related XML doc comment, and updated
the three affected backend test files.

While validating, discovered the test project failed to build for an unrelated,
pre-existing reason (see Notes) and fixed it in an isolated commit so the test suite
could actually run.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/Analytics/DailyInvoiceCount.cs` — removed `IsBelowThreshold` property.
- `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/DailyInvoiceCountDto.cs` — new DTO class (`Date`, `Count`, `IsBelowThreshold`), matches `TopProductDto.cs` style.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs` — replaced in-place mutation loop with a `Select` projection into `DailyInvoiceCountDto`.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsResponse.cs` — `Data` is now `List<DailyInvoiceCountDto>`.
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs` — removed the three dead `IsBelowThreshold = false` initializers.
- `backend/src/Anela.Heblo.Domain/Features/Analytics/IInvoiceImportStatisticsSource.cs` — updated XML doc comment.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs` — updated mocks/assertions for the new DTO shape.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/DashboardTiles/InvoiceImportStatisticsTileTests.cs` — dropped dead `IsBelowThreshold = false` initializers.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapterTests.cs` — removed the assertion on the now-removed property.
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — **out-of-scope fix, separate commit**: fixed a stale `ConfigurationConstants.APP_VERSION` reference to `InfrastructureConfigurationKeys.APP_VERSION` (pre-existing build break from an unrelated prior PR, unblocked here so the test project would compile).

## Tests
- `GetInvoiceImportStatisticsHandlerTests` — asserts `DailyInvoiceCountDto.IsBelowThreshold` is computed correctly for both above- and below-threshold days.
- `InvoiceImportStatisticsTileTests` — compiles and passes with the dead initializer removed.
- `InvoiceImportStatisticsSourceAdapterTests` — compiles and passes with the stale assertion removed.

## How to verify
```
cd backend
dotnet build Anela.Heblo.sln
cd test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~GetInvoiceImportStatisticsHandlerTests|FullyQualifiedName~InvoiceImportStatisticsTileTests|FullyQualifiedName~InvoiceImportStatisticsSourceAdapterTests"
```
Result: build succeeds (0 errors), 14/14 targeted tests pass. Full suite run: 5414 passed,
64 failed — all 64 failures are pre-existing Testcontainers/Docker-dependent integration
tests (Leaflet repository integration tests requiring a Postgres container), unrelated to
this change and failing because no Docker daemon is available in this sandbox.

## Notes
- The `dotnet build` on `main` (before this change) already failed with `error CS0117:
  'ConfigurationConstants' does not contain a definition for 'APP_VERSION'` in
  `GetConfigurationHandlerTests.cs` — a leftover from PR #3435 which renamed
  `ConfigurationConstants` to `InfrastructureConfigurationKeys` but missed this one call
  site. Fixed in an isolated commit (`fix(config): ...`) separate from the feature commit,
  since it's required to compile and run the test suite at all but is unrelated to issue
  #3463's Clean Architecture finding.
- Serialized JSON response shape is unchanged (`date`, `count`, `isBelowThreshold`,
  `minimumThreshold`) — only C# type names changed, per the spec.

## PR Summary
Removed an application-layer concern (`IsBelowThreshold`) from the Domain type
`DailyInvoiceCount`, per the arch-review finding in issue #3463. The threshold comparison —
previously mutated onto the Domain object by `GetInvoiceImportStatisticsHandler` using
`InvoiceImportOptions.MinimumDailyThreshold` — is now computed inline when projecting into
a new `DailyInvoiceCountDto` in the Application layer's `Contracts/` folder, keeping the
Domain type immutable and free of configuration awareness.

### Changes
- `DailyInvoiceCount.cs` (Domain) — dropped `IsBelowThreshold`.
- `DailyInvoiceCountDto.cs` (new) — carries `Date`, `Count`, `IsBelowThreshold`.
- `GetInvoiceImportStatisticsHandler.cs` / `GetInvoiceImportStatisticsResponse.cs` — project into the DTO instead of mutating the Domain object.
- `InvoiceImportStatisticsSourceAdapter.cs` — dropped now-dead property initializers.
- Backend tests updated to match; a small, isolated fix for an unrelated pre-existing build break in `GetConfigurationHandlerTests.cs`.

## Status
DONE
