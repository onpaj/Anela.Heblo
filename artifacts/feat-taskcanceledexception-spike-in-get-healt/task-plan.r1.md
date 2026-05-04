### task: reorder-di-registration-order

**Goal:** Move `AddPersistenceServices` ahead of `AddHealthCheckServices` in `Program.cs` so that `NpgsqlDataSource` is registered in DI before health checks try to detect/consume it.

**Context:**

`backend/src/Anela.Heblo.API/Program.cs` currently calls `AddHealthCheckServices` *before* `AddPersistenceServices`:

```csharp
// CURRENT ORDER (lines ~52–58)
builder.Services.ConfigureAuthentication(builder, logger);
builder.Services.AddApplicationInsightsServices(builder.Configuration, builder.Environment);
builder.Services.AddCorsServices(builder.Configuration);
builder.Services.AddHealthCheckServices(builder.Configuration);

// Add new architecture services
builder.Services.AddPersistenceServices(builder.Configuration, builder.Environment);
builder.Services.AddApplicationServices(builder.Configuration, builder.Environment);
```

`PersistenceModule.AddPersistenceServices` (`backend/src/Anela.Heblo.Persistence/PersistenceModule.cs:80-81`) registers the singleton `NpgsqlDataSource`. Subsequent tasks gate the database health check on `services.Any(d => d.ServiceType == typeof(NpgsqlDataSource))` — that probe only works correctly if persistence registers first. The reorder is required by the chosen DI-presence gating in the architecture review (Decision 2).

The two methods are independent in current code (`AddPersistenceServices` reads only `IConfiguration`/`IHostEnvironment`); reordering must not break `ApplicationStartupTests`.

**Files to create/modify:**
- `backend/src/Anela.Heblo.API/Program.cs` — move `AddPersistenceServices` call above `AddHealthCheckServices`.

**Implementation steps:**
1. Open `backend/src/Anela.Heblo.API/Program.cs`.
2. Locate the block that registers services around lines 52–58. Move the line:
   ```csharp
   builder.Services.AddPersistenceServices(builder.Configuration, builder.Environment);
   ```
   to execute **before**:
   ```csharp
   builder.Services.AddHealthCheckServices(builder.Configuration);
   ```
3. Final order should be:
   ```csharp
   builder.Services.ConfigureAuthentication(builder, logger);
   builder.Services.AddApplicationInsightsServices(builder.Configuration, builder.Environment);
   builder.Services.AddCorsServices(builder.Configuration);

   // Persistence must register NpgsqlDataSource before health checks probe DI for it.
   builder.Services.AddPersistenceServices(builder.Configuration, builder.Environment);

   builder.Services.AddHealthCheckServices(builder.Configuration);

   builder.Services.AddApplicationServices(builder.Configuration, builder.Environment);
   builder.Services.AddXccServices(builder.Configuration);
   builder.Services.AddCrossCuttingServices();
   builder.Services.AddSpaServices();
   ```
4. Run `dotnet build` on `backend/Anela.Heblo.sln` to confirm the project still compiles.
5. Run `dotnet test --filter "FullyQualifiedName~ApplicationStartupTests"` and confirm all tests pass.

**Tests to write:**
None directly for this task. Coverage is provided by existing `ApplicationStartupTests` (`backend/test/Anela.Heblo.Tests/ApplicationStartupTests.cs`) and by tasks below.

**Acceptance criteria:**
- `Program.cs` calls `AddPersistenceServices` before `AddHealthCheckServices`.
- `dotnet build` succeeds.
- `ApplicationStartupTests` pass without modification.

---

### task: add-health-checks-probe-timeout-config

**Goal:** Add the `HealthChecks:ProbeTimeoutSeconds` configuration key (default `5`) to `appsettings.json` so the per-check timeout is configurable per environment.

**Context:**

Spec FR-3 requires:
> A timeout of **5 seconds** is applied to the database health check and to `DataQualitySchemaHealthCheck` … The timeout value is exposed as a configuration setting `HealthChecks:ProbeTimeoutSeconds` with default `5`. If set to `0` or negative, no per-check timeout is applied (escape hatch for diagnostics).

Architecture decision 5 explicitly rejects an options class for a single integer setting:
> **Chosen approach:** A — inline `GetValue<int>` at this scale. A single integer setting with one consumer does not warrant the ceremony of an options class.

The new key must be added at the top level of `backend/src/Anela.Heblo.API/appsettings.json`. No environment-specific override files require updates (per spec: "No environment-specific overrides required initially").

**Files to create/modify:**
- `backend/src/Anela.Heblo.API/appsettings.json` — add the `HealthChecks` section.

**Implementation steps:**
1. Open `backend/src/Anela.Heblo.API/appsettings.json`.
2. Insert the following key at the top level (place it alphabetically near other top-level sections, e.g., right above `"Hangfire"`):
   ```json
   "HealthChecks": {
     "ProbeTimeoutSeconds": 5
   },
   ```
3. Validate the JSON is still well-formed (run `dotnet build` — JSON syntax errors will fail the build because the file is embedded as a resource via `appsettings.json` content inclusion).

**Tests to write:**
None for this task. The value is consumed and asserted in `task: rewire-health-check-services` and `task: add-health-check-registration-tests`.

**Acceptance criteria:**
- `appsettings.json` contains `"HealthChecks": { "ProbeTimeoutSeconds": 5 }`.
- File is valid JSON; `dotnet build` succeeds.
- No other configuration files (`appsettings.Development.json`, `appsettings.Production.json`, etc.) are modified by this task.

---

### task: rewire-health-check-services

**Goal:** Replace the raw-connection-string `AddNpgSql` registration in `AddHealthCheckServices` with the `NpgsqlDataSource`-factory overload, gate it on DI presence of `NpgsqlDataSource`, and apply a 5 s `Timeout` (sourced from configuration) to both DB-touching checks.

**Context:**

Current implementation in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` lines 80–99:

```csharp
public static IServiceCollection AddHealthCheckServices(this IServiceCollection services, IConfiguration configuration)
{
    var healthChecksBuilder = services.AddHealthChecks()
        .AddCheck<Anela.Heblo.Application.Common.BackgroundServicesReadyHealthCheck>("background-services-ready", tags: new[] { "ready" })
        .AddCheck<DataQualitySchemaHealthCheck>(
            name: "data-quality-schema",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "ready", "db", "schema" });

    // Add database health check if connection string exists
    var dbConnectionString = configuration.GetConnectionString(ConfigurationConstants.DEFAULT_CONNECTION);
    if (!string.IsNullOrEmpty(dbConnectionString))
    {
        healthChecksBuilder.AddNpgSql(dbConnectionString,
            name: ConfigurationConstants.DATABASE_HEALTH_CHECK,
            tags: new[] { ConfigurationConstants.DB_TAG, ConfigurationConstants.POSTGRESQL_TAG });
    }

    return services;
}
```

Spec FR-1 acceptance criteria:
- `AddHealthCheckServices` no longer reads `ConnectionStrings:DefaultConnection` for the purpose of registering `AddNpgSql`.
- The health check registration uses `AddNpgSql(sp => sp.GetRequiredService<NpgsqlDataSource>(), ...)` (or equivalent overload that accepts a data-source factory).
- The health check is still registered with `name = ConfigurationConstants.DATABASE_HEALTH_CHECK` and tags `{ ConfigurationConstants.DB_TAG, ConfigurationConstants.POSTGRESQL_TAG }`.
- The check is registered only when `NpgsqlDataSource` is present in DI. When absent, no DB health check is registered.

Spec FR-3 acceptance criteria:
- A timeout of **5 seconds** is applied to the database health check and to `DataQualitySchemaHealthCheck`.
- The timeout value is read from `HealthChecks:ProbeTimeoutSeconds` (default `5`). If set to `0` or negative, **no per-check timeout is applied** (escape hatch).

`PersistenceModule` registers `NpgsqlDataSource` only when `useInMemory` is false **and** `connectionString != "InMemory"`. Probing `services.Any(d => d.ServiceType == typeof(NpgsqlDataSource))` after `AddPersistenceServices` runs is the correct, single source of truth.

Constants used:
- `ConfigurationConstants.DATABASE_HEALTH_CHECK = "database"`
- `ConfigurationConstants.DB_TAG = "db"`
- `ConfigurationConstants.POSTGRESQL_TAG = "postgresql"`

The `AspNetCore.HealthChecks.NpgSql` 8.0.1 package supports `NpgsqlDataSource` factory registration (added in 7.0.0). If the exact `Func<IServiceProvider, NpgsqlDataSource>` overload is not present, the equivalent fallback is `AddNpgSql(sp => sp.GetRequiredService<NpgsqlDataSource>().CreateConnection(), ...)`.

**Files to create/modify:**
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — rewrite the body of `AddHealthCheckServices`.

**Implementation steps:**
1. Open `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`.
2. Confirm the `using Npgsql;` import is present at the top of the file (it already is).
3. Replace the body of `AddHealthCheckServices` (lines 80–99) with:
   ```csharp
   public static IServiceCollection AddHealthCheckServices(this IServiceCollection services, IConfiguration configuration)
   {
       var probeTimeoutSeconds = configuration.GetValue<int>("HealthChecks:ProbeTimeoutSeconds", 5);
       var probeTimeout = probeTimeoutSeconds > 0
           ? TimeSpan.FromSeconds(probeTimeoutSeconds)
           : default; // 0 or negative disables per-check timeout

       var healthChecksBuilder = services.AddHealthChecks()
           .AddCheck<Anela.Heblo.Application.Common.BackgroundServicesReadyHealthCheck>(
               "background-services-ready",
               tags: new[] { "ready" })
           .AddCheck<DataQualitySchemaHealthCheck>(
               name: "data-quality-schema",
               failureStatus: HealthStatus.Unhealthy,
               tags: new[] { "ready", "db", "schema" },
               timeout: probeTimeout);

       // Register the database health check only when an NpgsqlDataSource is registered
       // (PersistenceModule skips it when UseInMemoryDatabase=true or connectionString=="InMemory").
       var hasNpgsqlDataSource = services.Any(d => d.ServiceType == typeof(NpgsqlDataSource));
       if (hasNpgsqlDataSource)
       {
           healthChecksBuilder.AddNpgSql(
               sp => sp.GetRequiredService<NpgsqlDataSource>(),
               name: ConfigurationConstants.DATABASE_HEALTH_CHECK,
               failureStatus: HealthStatus.Unhealthy,
               tags: new[] { ConfigurationConstants.DB_TAG, ConfigurationConstants.POSTGRESQL_TAG },
               timeout: probeTimeout);
       }

       return services;
   }
   ```
4. If `dotnet build` reports that `AddNpgSql(Func<IServiceProvider, NpgsqlDataSource>, ...)` cannot be resolved, fall back to the equivalent `NpgsqlConnection`-factory overload (same pool, same outcome):
   ```csharp
   healthChecksBuilder.AddNpgSql(
       sp => sp.GetRequiredService<NpgsqlDataSource>().CreateConnection(),
       name: ConfigurationConstants.DATABASE_HEALTH_CHECK,
       failureStatus: HealthStatus.Unhealthy,
       tags: new[] { ConfigurationConstants.DB_TAG, ConfigurationConstants.POSTGRESQL_TAG },
       timeout: probeTimeout);
   ```
5. Run `dotnet build` to verify compilation.

**Tests to write:**
None directly in this task. Coverage is added in `task: add-health-check-registration-tests` (resolves shared `NpgsqlDataSource`, gates on InMemory, asserts `Timeout=5s`).

**Acceptance criteria:**
- `AddHealthCheckServices` no longer reads `ConfigurationConstants.DEFAULT_CONNECTION` from `ConnectionStrings`.
- The `database` registration uses `sp.GetRequiredService<NpgsqlDataSource>()` (or the `NpgsqlConnection` fallback that derives from the same data source).
- The `database` registration uses `name = ConfigurationConstants.DATABASE_HEALTH_CHECK` and tags `{ "db", "postgresql" }`.
- The `database` registration is omitted when `NpgsqlDataSource` is absent in `IServiceCollection`.
- Both `data-quality-schema` and `database` registrations have `Timeout = 5s` by default; `Timeout = default` (no timeout) when `HealthChecks:ProbeTimeoutSeconds <= 0`.
- `dotnet build` succeeds.

---

### task: harden-data-quality-schema-health-check

**Goal:** Add an explicit `catch (OperationCanceledException)` clause to `DataQualitySchemaHealthCheck` that returns `HealthCheckResult.Degraded("DataQuality probe was cancelled")` and emits an Information-level structured log entry, without an exception payload.

**Context:**

Current implementation (`backend/src/Anela.Heblo.API/HealthChecks/DataQuality/DataQualitySchemaHealthCheck.cs`):

```csharp
public sealed class DataQualitySchemaHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _db;

    public DataQualitySchemaHealthCheck(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.DqtRuns.AsNoTracking().AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy("DataQuality schema is reachable");
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            return HealthCheckResult.Unhealthy(
                description: "DataQuality table not found",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    ["entity"] = "DqtRun",
                    ["expectedTable"] = "DqtRuns",
                    ["schema"] = "public",
                    ["sqlState"] = "42P01"
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                description: "DataQuality probe failed",
                exception: ex);
        }
    }
}
```

Spec FR-2 acceptance criteria:
- The new catch returns `HealthCheckResult.Degraded("DataQuality probe was cancelled")` with **no `exception` payload**.
- The catch must observe the host-supplied `cancellationToken`. If the host did not request cancellation but the exception still fires, the result must remain `Degraded`.
- The existing `PostgresException` (`SqlState == "42P01"`) catch and the generic `Exception` catch remain unchanged in behavior and ordering.

Spec NFR-4 / arch-review amendment 7: the structured log MUST be Information level (not Warning) and MUST omit the exception object — the goal is suppression of App Insights exception telemetry. Properties `{ ProbeName, ElapsedMs }`.

The catch order is **load-bearing**: `OperationCanceledException` must be caught **between** the `PostgresException` catch and the generic `Exception` catch. Placing it after the generic catch makes it unreachable.

**Files to create/modify:**
- `backend/src/Anela.Heblo.API/HealthChecks/DataQuality/DataQualitySchemaHealthCheck.cs` — inject `ILogger<DataQualitySchemaHealthCheck>`, add elapsed-time tracking, add the `OperationCanceledException` catch.

**Implementation steps:**
1. Open `backend/src/Anela.Heblo.API/HealthChecks/DataQuality/DataQualitySchemaHealthCheck.cs`.
2. Add `using Microsoft.Extensions.Logging;` and `using System.Diagnostics;` to the imports if missing.
3. Replace the class with:
   ```csharp
   public sealed class DataQualitySchemaHealthCheck : IHealthCheck
   {
       private const string ProbeName = "data-quality-schema";

       private readonly ApplicationDbContext _db;
       private readonly ILogger<DataQualitySchemaHealthCheck> _logger;

       public DataQualitySchemaHealthCheck(
           ApplicationDbContext db,
           ILogger<DataQualitySchemaHealthCheck> logger)
       {
           _db = db;
           _logger = logger;
       }

       public async Task<HealthCheckResult> CheckHealthAsync(
           HealthCheckContext context,
           CancellationToken cancellationToken = default)
       {
           var stopwatch = Stopwatch.StartNew();
           try
           {
               await _db.DqtRuns.AsNoTracking().AnyAsync(cancellationToken);
               return HealthCheckResult.Healthy("DataQuality schema is reachable");
           }
           catch (PostgresException ex) when (ex.SqlState == "42P01")
           {
               return HealthCheckResult.Unhealthy(
                   description: "DataQuality table not found",
                   exception: ex,
                   data: new Dictionary<string, object>
                   {
                       ["entity"] = "DqtRun",
                       ["expectedTable"] = "DqtRuns",
                       ["schema"] = "public",
                       ["sqlState"] = "42P01"
                   });
           }
           catch (OperationCanceledException)
           {
               _logger.LogInformation(
                   "DataQuality probe cancelled. ProbeName={ProbeName}, ElapsedMs={ElapsedMs}",
                   ProbeName,
                   stopwatch.ElapsedMilliseconds);
               return HealthCheckResult.Degraded("DataQuality probe was cancelled");
           }
           catch (Exception ex)
           {
               return HealthCheckResult.Unhealthy(
                   description: "DataQuality probe failed",
                   exception: ex);
           }
       }
   }
   ```
4. The new catch block must sit **between** the `PostgresException` catch and the generic `Exception` catch — this ordering is required so the more-specific `OperationCanceledException` is observed before falling through to the generic handler.
5. Run `dotnet build`.

**Tests to write:**
None in this task — see `task: extend-data-quality-schema-tests` for the cancellation/logger test cases. The existing two tests in `DataQualitySchemaHealthCheckTests.cs` (healthy + 42P01) must continue to pass after this change; update only the constructor call.

**Acceptance criteria:**
- `DataQualitySchemaHealthCheck` constructor takes `ILogger<DataQualitySchemaHealthCheck>` in addition to `ApplicationDbContext`.
- The class catches `OperationCanceledException` between the `PostgresException` and generic `Exception` catches.
- The cancellation catch returns `HealthCheckResult.Degraded("DataQuality probe was cancelled")` with no exception payload.
- One Information-level log is emitted per cancellation, carrying the structured properties `ProbeName="data-quality-schema"` and `ElapsedMs=<long>`. No exception object is logged.
- Existing `PostgresException` and generic `Exception` catches are byte-for-byte unchanged in behavior.
- `dotnet build` succeeds.

---

### task: extend-data-quality-schema-tests

**Goal:** Add unit tests covering the new `OperationCanceledException` path on `DataQualitySchemaHealthCheck`, and update the existing tests to match the new constructor signature.

**Context:**

Existing test file: `backend/test/Anela.Heblo.Tests/API/HealthChecks/DataQuality/DataQualitySchemaHealthCheckTests.cs` already contains:
- `CheckHealthAsync_WhenTableReachable_ReturnsHealthy`
- `CheckHealthAsync_WhenTableMissing_ReturnsUnhealthyWith42P01Data`
- `CheckHealthAsync_WhenOtherException_ReturnsUnhealthyWithRawException`

It uses Moq + EF Core in-memory DB, FluentAssertions, and a `BuildThrowingDbSet<T>(Exception)` helper.

The constructor of `DataQualitySchemaHealthCheck` now requires `ILogger<DataQualitySchemaHealthCheck>` in addition to `ApplicationDbContext`. All existing tests must be updated to pass a logger (use `NullLogger<DataQualitySchemaHealthCheck>.Instance` for tests that do not assert on logs, and a `Mock<ILogger<DataQualitySchemaHealthCheck>>` for the new cancellation test that asserts the log is emitted).

Spec FR-2 acceptance criteria require unit tests covering three paths: (a) healthy, (b) cancelled token → `Degraded`, (c) generic exception → `Unhealthy`. (a) and (c) already exist; this task adds (b) plus a logger-emission assertion per NFR-4.

**Files to create/modify:**
- `backend/test/Anela.Heblo.Tests/API/HealthChecks/DataQuality/DataQualitySchemaHealthCheckTests.cs` — update existing tests for the new constructor; add cancellation-path tests.

**Implementation steps:**
1. Open `backend/test/Anela.Heblo.Tests/API/HealthChecks/DataQuality/DataQualitySchemaHealthCheckTests.cs`.
2. Add the imports (if not already present):
   ```csharp
   using Microsoft.Extensions.Logging;
   using Microsoft.Extensions.Logging.Abstractions;
   ```
3. Update each of the three existing tests so the `DataQualitySchemaHealthCheck` constructor receives `NullLogger<DataQualitySchemaHealthCheck>.Instance`:
   ```csharp
   var healthCheck = new DataQualitySchemaHealthCheck(context, NullLogger<DataQualitySchemaHealthCheck>.Instance);
   ```
4. Append two new tests:

   ```csharp
   [Fact]
   public async Task CheckHealthAsync_WhenCancellationTokenFires_ReturnsDegradedWithoutException()
   {
       // Arrange
       var options = new DbContextOptionsBuilder<ApplicationDbContext>()
           .UseInMemoryDatabase(databaseName: $"cancelled-{Guid.NewGuid()}")
           .Options;
       await using var context = new ApplicationDbContext(options);
       context.DqtRuns = BuildThrowingDbSet<DqtRun>(new OperationCanceledException("probe cancelled"));

       var loggerMock = new Mock<ILogger<DataQualitySchemaHealthCheck>>();
       var healthCheck = new DataQualitySchemaHealthCheck(context, loggerMock.Object);

       using var cts = new CancellationTokenSource();
       cts.Cancel();

       // Act
       var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), cts.Token);

       // Assert
       result.Status.Should().Be(HealthStatus.Degraded);
       result.Description.Should().Be("DataQuality probe was cancelled");
       result.Exception.Should().BeNull();
   }

   [Fact]
   public async Task CheckHealthAsync_WhenCancelled_LogsInformationWithProbeNameAndElapsed()
   {
       // Arrange
       var options = new DbContextOptionsBuilder<ApplicationDbContext>()
           .UseInMemoryDatabase(databaseName: $"cancelled-log-{Guid.NewGuid()}")
           .Options;
       await using var context = new ApplicationDbContext(options);
       context.DqtRuns = BuildThrowingDbSet<DqtRun>(new TaskCanceledException());

       var loggerMock = new Mock<ILogger<DataQualitySchemaHealthCheck>>();
       var healthCheck = new DataQualitySchemaHealthCheck(context, loggerMock.Object);

       // Act
       await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

       // Assert: exactly one Information log with ProbeName + ElapsedMs and NO exception payload.
       loggerMock.Verify(
           x => x.Log(
               LogLevel.Information,
               It.IsAny<EventId>(),
               It.Is<It.IsAnyType>((v, _) =>
                   v.ToString()!.Contains("data-quality-schema")
                   && v.ToString()!.Contains("ElapsedMs")),
               null, // no exception object should be passed
               It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
           Times.Once);
       loggerMock.VerifyNoOtherCalls();
   }
   ```
5. Run `dotnet test --filter "FullyQualifiedName~DataQualitySchemaHealthCheckTests"` and confirm all five tests pass.

**Tests to write:**
- `CheckHealthAsync_WhenTableReachable_ReturnsHealthy` — existing, updated for new constructor.
- `CheckHealthAsync_WhenTableMissing_ReturnsUnhealthyWith42P01Data` — existing, updated for new constructor.
- `CheckHealthAsync_WhenOtherException_ReturnsUnhealthyWithRawException` — existing, updated for new constructor.
- `CheckHealthAsync_WhenCancellationTokenFires_ReturnsDegradedWithoutException` — new. Asserts `HealthStatus.Degraded`, description `"DataQuality probe was cancelled"`, `result.Exception == null`.
- `CheckHealthAsync_WhenCancelled_LogsInformationWithProbeNameAndElapsed` — new. Asserts exactly one `LogLevel.Information` call carrying the literal `"data-quality-schema"` and `"ElapsedMs"` in the formatted state, with **no exception object**.

**Acceptance criteria:**
- All five tests pass.
- The cancellation-path test verifies `Status==Degraded`, description text, and `Exception==null`.
- The logger-verification test asserts Information level, presence of `ProbeName` and `ElapsedMs` markers in the message, and that the `exception` parameter to `ILogger.Log` is `null`.

---

### task: add-health-check-registration-tests

**Goal:** Add a composition-root test class asserting that the `database` health check is wired to the singleton `NpgsqlDataSource`, that it is **not** registered when `UseInMemoryDatabase=true`, and that both DB-touching checks expose `Timeout = 5s` when the default config is in effect.

**Context:**

Spec FR-1 acceptance criterion:
> A unit/integration test asserts the health check is wired to the singleton `NpgsqlDataSource` (e.g., the same instance returned by `sp.GetRequiredService<NpgsqlDataSource>()`).

Spec FR-3 implies `Timeout` is observable via `HealthCheckRegistration.Timeout`.

Arch-review amendment 6:
> Add `HealthCheckRegistrationTests` (composition-root test) that asserts (a) the `database` check resolves the same `NpgsqlDataSource` instance as the rest of DI, (b) the `database` check is *not* registered when `UseInMemoryDatabase=true`, and (c) `data-quality-schema` and `database` registrations expose `Timeout = 5s`.

`PersistenceModule.AddPersistenceServices` requires a connection string named after `IHostEnvironment.EnvironmentName`. The test must build a service collection with both modules registered (in the order `AddPersistenceServices` → `AddHealthCheckServices`, matching `Program.cs`). For the "real Postgres" branch the connection string can be a syntactically valid but non-connected one — `NpgsqlDataSource.Build()` does not open a connection at build time. For the InMemory branch, set `"UseInMemoryDatabase": "true"`.

`HealthCheckRegistration` instances are accessible by resolving `IOptions<HealthCheckServiceOptions>` and reading `Value.Registrations`.

Verifying that `database` resolves the same `NpgsqlDataSource`: the package-internal `AddNpgSql(Func<IServiceProvider, NpgsqlDataSource>, ...)` registers a `HealthCheckRegistration` whose factory invokes the supplied `Func<IServiceProvider, ...>`. Since the factory captures `sp.GetRequiredService<NpgsqlDataSource>()`, two consecutive resolutions of the singleton from the same `IServiceProvider` must return reference-equal instances. The check itself (the `IHealthCheck` returned by the factory) is internal; the test asserts the **shared singleton** by resolving `NpgsqlDataSource` from the built provider twice and confirming reference equality, plus asserts the registration with name `"database"` exists.

`Anela.Heblo.Tests` already references `Anela.Heblo.Persistence`, `Anela.Heblo.API`, FluentAssertions, and xUnit (existing test files use them).

**Files to create/modify:**
- `backend/test/Anela.Heblo.Tests/API/HealthChecks/HealthCheckRegistrationTests.cs` — new test class.

**Implementation steps:**
1. Create `backend/test/Anela.Heblo.Tests/API/HealthChecks/HealthCheckRegistrationTests.cs` with:
   ```csharp
   using System.Linq;
   using Anela.Heblo.API.Extensions;
   using Anela.Heblo.Domain.Features.Configuration;
   using Anela.Heblo.Persistence;
   using FluentAssertions;
   using Microsoft.Extensions.Configuration;
   using Microsoft.Extensions.DependencyInjection;
   using Microsoft.Extensions.Diagnostics.HealthChecks;
   using Microsoft.Extensions.Hosting;
   using Microsoft.Extensions.Logging;
   using Microsoft.Extensions.Logging.Abstractions;
   using Microsoft.Extensions.Options;
   using Moq;
   using Npgsql;
   using Xunit;

   namespace Anela.Heblo.Tests.API.HealthChecks;

   public class HealthCheckRegistrationTests
   {
       [Fact]
       public void DatabaseCheck_IsRegistered_AndResolvesSingletonNpgsqlDataSource()
       {
           // Arrange: build a service collection mirroring Program.cs order.
           var services = BuildServices(useInMemory: false);

           // Act
           using var provider = services.BuildServiceProvider();
           var ds1 = provider.GetRequiredService<NpgsqlDataSource>();
           var ds2 = provider.GetRequiredService<NpgsqlDataSource>();
           var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
               .Value.Registrations.ToList();

           // Assert
           ds1.Should().BeSameAs(ds2, "NpgsqlDataSource must be a singleton shared by all consumers");
           registrations.Should().Contain(r => r.Name == ConfigurationConstants.DATABASE_HEALTH_CHECK);
       }

       [Fact]
       public void DatabaseCheck_IsNotRegistered_WhenUseInMemoryDatabaseIsTrue()
       {
           // Arrange
           var services = BuildServices(useInMemory: true);

           // Act
           using var provider = services.BuildServiceProvider();
           var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
               .Value.Registrations.ToList();

           // Assert
           services.Any(d => d.ServiceType == typeof(NpgsqlDataSource)).Should().BeFalse();
           registrations.Should().NotContain(r => r.Name == ConfigurationConstants.DATABASE_HEALTH_CHECK);
       }

       [Fact]
       public void DataQualityAndDatabaseChecks_HaveFiveSecondTimeout_ByDefault()
       {
           // Arrange
           var services = BuildServices(useInMemory: false);

           // Act
           using var provider = services.BuildServiceProvider();
           var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
               .Value.Registrations.ToList();

           // Assert
           var dataQuality = registrations.Single(r => r.Name == "data-quality-schema");
           var database = registrations.Single(r => r.Name == ConfigurationConstants.DATABASE_HEALTH_CHECK);
           dataQuality.Timeout.Should().Be(TimeSpan.FromSeconds(5));
           database.Timeout.Should().Be(TimeSpan.FromSeconds(5));
       }

       private static IServiceCollection BuildServices(bool useInMemory)
       {
           var configValues = new Dictionary<string, string?>
           {
               ["UseInMemoryDatabase"] = useInMemory ? "true" : "false",
               // PersistenceModule reads ConnectionStrings:<EnvironmentName>
               ["ConnectionStrings:UnitTest"] = useInMemory
                   ? "InMemory"
                   : "Host=localhost;Database=heblo_test;Username=u;Password=p",
               // Default 5s probe timeout (no override)
           };
           var configuration = new ConfigurationBuilder()
               .AddInMemoryCollection(configValues)
               .Build();

           var environment = new Mock<IHostEnvironment>();
           environment.SetupGet(e => e.EnvironmentName).Returns("UnitTest");

           var services = new ServiceCollection();
           services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
           services.AddPersistenceServices(configuration, environment.Object);
           services.AddHealthCheckServices(configuration);
           return services;
       }
   }
   ```
2. Run `dotnet test --filter "FullyQualifiedName~HealthCheckRegistrationTests"` and confirm all three tests pass.

**Tests to write:**
- `DatabaseCheck_IsRegistered_AndResolvesSingletonNpgsqlDataSource` — builds the service collection with `UseInMemoryDatabase=false`, asserts that two resolutions of `NpgsqlDataSource` yield the same instance and that a registration named `"database"` exists.
- `DatabaseCheck_IsNotRegistered_WhenUseInMemoryDatabaseIsTrue` — builds with `UseInMemoryDatabase=true`, asserts that `NpgsqlDataSource` is absent from `IServiceCollection` and that no registration named `"database"` exists.
- `DataQualityAndDatabaseChecks_HaveFiveSecondTimeout_ByDefault` — asserts `data-quality-schema` and `database` registrations both have `Timeout = TimeSpan.FromSeconds(5)`.

**Acceptance criteria:**
- All three tests pass.
- Tests do not require an actual Postgres connection (only the data-source build, which is offline).
- Failure of any of the three tests indicates a regression in DI ordering, the InMemory gating, or the timeout config plumbing.

---

### task: verify-degraded-maps-to-http-200

**Goal:** Add an integration test that confirms the `UIResponseWriter.WriteHealthCheckUIResponse` writer maps a `Degraded`-only health report to HTTP 200, locking in the architectural assumption underpinning FR-2 (cancelled probe → `Degraded` → keep instance in rotation).

**Context:**

Arch-review **acceptance gate** (Specification Amendment 2):
> Add an integration test in `Anela.Heblo.Tests` that posts a synthetic `Degraded` health-check report through `UIResponseWriter` and asserts HTTP 200. **This test must pass; otherwise FR-2 reverts to `Unhealthy` without exception payload.** The test is acceptance-blocking.

Spec NFR-3:
> The `Degraded` result for cancelled probes must not cause Azure App Service to restart the container. `Degraded` is rendered as HTTP 200 by the default response writer; verify the UI response writer used here also maps `Degraded` to a non-503 status.

The minimal test setup:
- Stand up a `WebHostBuilder`/`TestServer` configured with a single fake health check returning `HealthCheckResult.Degraded(...)`.
- Map `/health/ready` exactly as `ApplicationBuilderExtensions.ConfigureHealthCheckEndpoints` does — predicate on `Tags.Contains("ready")` and `ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse`.
- Issue an HTTP GET against `/health/ready`.
- Assert `response.StatusCode == HttpStatusCode.OK`.

`UIResponseWriter` lives in the package `HealthChecks.UI.Client`. The `Anela.Heblo.API` project references it (per existing `using HealthChecks.UI.Client;` in `ApplicationBuilderExtensions`).

This test is small, self-contained, and does not require booting the full application. Use `Microsoft.AspNetCore.TestHost` (already a transitive dependency of `Microsoft.AspNetCore.Mvc.Testing`, used elsewhere in `Anela.Heblo.Tests`).

If this test **fails** (i.e., `UIResponseWriter` returns 503 for `Degraded`), the implementer must change `task: harden-data-quality-schema-health-check` to return `HealthCheckResult.Unhealthy("DataQuality probe was cancelled")` *without* an exception payload. Document the divergence and re-run the cancellation tests in `task: extend-data-quality-schema-tests` accordingly. **This branch is the spec's documented escape hatch; do not silently change behavior — call it out in the PR.**

**Files to create/modify:**
- `backend/test/Anela.Heblo.Tests/API/HealthChecks/UIResponseWriterStatusMappingTests.cs` — new test class.

**Implementation steps:**
1. Create `backend/test/Anela.Heblo.Tests/API/HealthChecks/UIResponseWriterStatusMappingTests.cs`:
   ```csharp
   using System.Net;
   using System.Threading;
   using System.Threading.Tasks;
   using FluentAssertions;
   using HealthChecks.UI.Client;
   using Microsoft.AspNetCore.Builder;
   using Microsoft.AspNetCore.Diagnostics.HealthChecks;
   using Microsoft.AspNetCore.Hosting;
   using Microsoft.AspNetCore.TestHost;
   using Microsoft.Extensions.DependencyInjection;
   using Microsoft.Extensions.Diagnostics.HealthChecks;
   using Microsoft.Extensions.Hosting;
   using Xunit;

   namespace Anela.Heblo.Tests.API.HealthChecks;

   public class UIResponseWriterStatusMappingTests
   {
       [Fact]
       public async Task UIResponseWriter_MapsDegradedReport_ToHttp200()
       {
           using var host = await new HostBuilder()
               .ConfigureWebHost(webBuilder =>
               {
                   webBuilder
                       .UseTestServer()
                       .ConfigureServices(services =>
                       {
                           services.AddHealthChecks()
                               .AddCheck(
                                   name: "fake-degraded",
                                   check: () => HealthCheckResult.Degraded("synthetic degraded"),
                                   tags: new[] { "ready" });
                       })
                       .Configure(app =>
                       {
                           app.UseRouting();
                           app.UseEndpoints(endpoints =>
                           {
                               endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
                               {
                                   Predicate = c => c.Tags.Contains("ready"),
                                   ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
                               });
                           });
                       });
               })
               .StartAsync();

           var client = host.GetTestClient();
           var response = await client.GetAsync("/health/ready");

           response.StatusCode.Should().Be(HttpStatusCode.OK,
               "UIResponseWriter must map a Degraded-only health report to 200; "
               + "if this assertion ever fails, FR-2 must change to return Unhealthy without an exception payload.");
       }
   }
   ```
2. Run `dotnet test --filter "FullyQualifiedName~UIResponseWriterStatusMappingTests"`.
3. If the assertion passes (`HttpStatusCode.OK`): the design assumption holds; FR-2 implementation in `task: harden-data-quality-schema-health-check` is correct as written.
4. If the assertion fails (e.g., 503): **stop.** Revisit `task: harden-data-quality-schema-health-check` and replace the `Degraded` return with:
   ```csharp
   return HealthCheckResult.Unhealthy("DataQuality probe was cancelled");
   ```
   (no exception payload). Update the cancellation tests in `task: extend-data-quality-schema-tests` to assert `HealthStatus.Unhealthy` instead of `HealthStatus.Degraded`, and re-run the full test suite.

**Tests to write:**
- `UIResponseWriter_MapsDegradedReport_ToHttp200` — boots a minimal `TestServer` with a single check returning `Degraded`, GETs `/health/ready`, asserts `HttpStatusCode.OK`.

**Acceptance criteria:**
- The test runs and passes against the currently referenced `HealthChecks.UI.Client` version. If it fails, the implementer follows the documented escape hatch (Unhealthy without exception payload), updates the cancellation tests, and notes the divergence in the PR description.
- `dotnet build` and `dotnet test` for the full `Anela.Heblo.Tests` project succeed end-to-end after every preceding task plus this one.