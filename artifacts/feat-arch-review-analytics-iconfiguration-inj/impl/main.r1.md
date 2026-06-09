All four tasks are complete. Here is the output summary:

---

# Implementation: Replace `IConfiguration` with `IOptions<InvoiceImportOptions>` in `GetInvoiceImportStatisticsHandler`

## What was implemented

Refactored the Analytics module to replace direct `IConfiguration` injection in `GetInvoiceImportStatisticsHandler` with a typed `InvoiceImportOptions` class bound via the standard .NET Options pattern. This removes the infrastructure dependency from the Application layer and eliminates the need for `ConfigurationBuilder` boilerplate in unit tests.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Analytics/InvoiceImportOptions.cs` — new options POCO with `ConfigurationKey = "InvoiceImport"`, `MinimumDailyThreshold = 10`, `DefaultDaysBack = 14`
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — added `IConfiguration configuration` parameter, added `services.Configure<InvoiceImportOptions>(...)` as first registration
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — updated `services.AddAnalyticsModule()` → `services.AddAnalyticsModule(configuration)` at line 74
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs` — replaced `IConfiguration` field/ctor param with `IOptions<InvoiceImportOptions>`, reads `_options.MinimumDailyThreshold` / `_options.DefaultDaysBack`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs` — replaced `ConfigurationBuilder` setup with `Options.Create(new InvoiceImportOptions {...})` in all 4 existing tests; added `Handle_ShouldUseDefaultValuesWhenOptionsAreParameterless` defaults-lock test

## Tests

`GetInvoiceImportStatisticsHandlerTests.cs` — 5 test methods (6 runs due to `[Theory]`), all green:
- `Handle_ShouldReturnStatisticsWithMinimumThreshold`
- `Handle_ShouldUseDefaultThresholdWhenNotConfigured`
- `Handle_ShouldUseConfigurableDefaultDaysBack`
- `Handle_ShouldPassCorrectDateTypeToRepository` (2 InlineData cases)
- `Handle_ShouldUseDefaultValuesWhenOptionsAreParameterless` (new)

## How to verify

```bash
dotnet build backend/Anela.Heblo.Backend.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetInvoiceImportStatisticsHandlerTests"
```

## Notes

- No new package references were needed — `Microsoft.Extensions.Configuration.Binder` was already in the Application project
- Minor test overlap exists between the defaults-lock test and `Handle_ShouldUseDefaultThresholdWhenNotConfigured` (both verify threshold=10 with empty options), noted as low-severity maintenance concern by code quality reviewer — not a correctness issue
- `InvoiceImportStatisticsTile` confirmed unaffected (does not read `InvoiceImport:*` config)

## PR Summary

Refactored `GetInvoiceImportStatisticsHandler` to use `IOptions<InvoiceImportOptions>` instead of raw `IConfiguration`, restoring Clean Architecture boundaries in the Application layer. The handler no longer depends on `Microsoft.Extensions.Configuration` — it reads typed properties from a POCO that is bound once in `AnalyticsModule` at DI registration time. Unit tests now construct options with `Options.Create(new InvoiceImportOptions {...})` instead of building an `IConfigurationRoot`, and a new defaults-lock test protects the 10/14 fallback values explicitly.

### Changes
- `Features/Analytics/InvoiceImportOptions.cs` — new typed options class with `ConfigurationKey` constant and defaults
- `Features/Analytics/AnalyticsModule.cs` — accepts `IConfiguration`, binds `InvoiceImportOptions` to `"InvoiceImport"` section
- `ApplicationModule.cs` — passes `configuration` to `AddAnalyticsModule`
- `UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs` — constructor and reads migrated to typed options
- `Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs` — all tests migrated to `Options.Create`, one new defaults-lock test added

## Status
DONE