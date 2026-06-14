# Resilient HomeAssistant Dependency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden `HomeAssistantConditionsReadingProvider` with Polly retries, a last-known-good stale fallback, suppressed-noise telemetry, an in-memory health check, and a snapshot-source custom metric so intermittent Tailscale outages stop flooding Application Insights.

**Architecture:** Resilience is configured on the typed `HttpClient` via `AddResilienceHandler` (per-attempt timeout + jittered retry). A singleton `HomeAssistantSnapshotCoordinator` exposes a single-flight `SemaphoreSlim` plus two snapshot slots (last-observed for health, last-known-good `Live` for fallback). An `ITelemetryProcessor` drops `DependencyTelemetry` flagged by a retry-handler `Activity` tag, so only the final attempt per sensor produces a dependency record. A `Meter`-backed counter emits `homeassistant.snapshot.source`; an `IHealthCheck` reads only the coordinator.

**Tech Stack:** .NET 8, `Microsoft.Extensions.Http.Resilience` (Polly v8), `Microsoft.Extensions.Diagnostics.HealthChecks`, `Microsoft.ApplicationInsights` (existing), `System.Diagnostics.Metrics.Meter`, xUnit + FluentAssertions + Moq.

---

## File Structure

### New files
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Resilience/HomeAssistantTransientErrorPredicate.cs` — predicate identifying retryable failures.
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Resilience/HomeAssistantRetryActivityTaggingHandler.cs` — `DelegatingHandler` that tags the per-attempt `Activity` when the HTTP send throws.
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Caching/HomeAssistantSnapshotCoordinator.cs` — singleton holding single-flight gate, last-observed snapshot, and last-known-good `Live` snapshot.
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HealthChecks/HomeAssistantConditionsHealthCheck.cs` — `IHealthCheck` reading the coordinator only.
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Telemetry/HomeAssistantSnapshotMetrics.cs` — `Meter`-backed counter.
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Telemetry/HomeAssistantDependencyTelemetryFilter.cs` — `ITelemetryProcessor` that drops `DependencyTelemetry` carrying the suppress property.
- `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantSnapshotCoordinatorTests.cs`
- `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantConditionsHealthCheckTests.cs`
- `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantDependencyTelemetryFilterTests.cs`
- `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantRetryPipelineTests.cs`

### Modified files
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/ConditionsReadingSource.cs` — append `Stale = 4`.
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantSettings.cs` — add `RetryCount`, `RetryBaseDelayMilliseconds`, `RetryMaxDelaySeconds`, `StaleSnapshotMaxAgeMinutes`, `LiveSnapshotMaxAgeMinutes`.
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantConditionsReadingProvider.cs` — single-flight, stale fallback, structured info log, demoted log levels; no try/catch around HTTP (Polly handles transients, only `OperationCanceledException` for caller cancellation passes through).
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantAdapterServiceCollectionExtensions.cs` — `AddResilienceHandler`, infinite client timeout, register coordinator + metrics + health check + tagging handler.
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Anela.Heblo.Adapters.HomeAssistant.csproj` — add three package references.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — register HA health check with tags `{ "homeassistant", "ready" }`.
- `backend/src/Anela.Heblo.API/Extensions/ApplicationInsightsExtensions.cs` — conditionally register `HomeAssistantDependencyTelemetryFilter`.
- `backend/src/Anela.Heblo.API/PDFPrints/ManufactureProtocolDocument.cs` — add `Stale => " (Starší údaje)"` switch arm.
- `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj` — add `Microsoft.AspNetCore.Diagnostics.HealthChecks.Abstractions` and `Microsoft.ApplicationInsights` references for new tests.
- `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantConditionsReadingProviderTests.cs` — extend with retry, stale-fallback, cold-start, single-flight, partial-does-not-overwrite-LKG, cancellation, and logging tests.

---

## Task 1: Add `Stale` value to `ConditionsReadingSource`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/ConditionsReadingSource.cs`

- [ ] **Step 1: Append the new enum member**

Replace the file contents with:

```csharp
namespace Anela.Heblo.Domain.Features.Manufacture.Conditions;

public enum ConditionsReadingSource
{
    Live = 1,
    Partial = 2,
    Unavailable = 3,
    Stale = 4,
}
```

- [ ] **Step 2: Build the Domain project to confirm no callers break**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/ConditionsReadingSource.cs
git commit -m "feat: add Stale value to ConditionsReadingSource"
```

---

## Task 2: Add Stale switch arm to `ManufactureProtocolDocument`

The PDF protocol's `SourceSuffix` currently falls through to `string.Empty` for unknown values — `Stale` would silently render with no suffix. Add an explicit arm.

**Files:**
- Modify: `backend/src/Anela.Heblo.API/PDFPrints/ManufactureProtocolDocument.cs:276-282`

- [ ] **Step 1: Extend the switch expression**

Replace lines 276-282:

```csharp
    private static string SourceSuffix(ConditionsReadingSource source) => source switch
    {
        ConditionsReadingSource.Live => string.Empty,
        ConditionsReadingSource.Partial => " (Částečné)",
        ConditionsReadingSource.Unavailable => " (HA nedostupný)",
        ConditionsReadingSource.Stale => " (Starší údaje)",
        _ => string.Empty,
    };
```

- [ ] **Step 2: Build the API project**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/PDFPrints/ManufactureProtocolDocument.cs
git commit -m "feat: render Stale source suffix in manufacture protocol PDF"
```

---

## Task 3: Extend `HomeAssistantSettings` with new knobs

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantSettings.cs`

- [ ] **Step 1: Add the new properties**

Replace the file contents with:

```csharp
namespace Anela.Heblo.Adapters.HomeAssistant;

public class HomeAssistantSettings
{
    public static string ConfigurationKey => "HomeAssistant";

    public string BaseUrl { get; init; } = null!;
    public string AccessToken { get; init; } = null!;
    public string InnerTemperatureEntityId { get; init; } = null!;
    public string InnerHumidityEntityId { get; init; } = null!;
    public string OuterTemperatureEntityId { get; init; } = null!;
    public string OuterHumidityEntityId { get; init; } = null!;
    public int RequestTimeoutSeconds { get; init; } = 3;
    public int ConditionsCacheDurationMinutes { get; init; } = 5;

    /// <summary>Polly retry attempts on transient HTTP failures. 0 disables retry.</summary>
    public int RetryCount { get; init; } = 2;

    /// <summary>Base delay (ms) for exponential backoff between retry attempts.</summary>
    public int RetryBaseDelayMilliseconds { get; init; } = 200;

    /// <summary>Upper bound (seconds) on the jittered backoff between retry attempts.</summary>
    public int RetryMaxDelaySeconds { get; init; } = 2;

    /// <summary>Last-known-good snapshot is reused for up to this many minutes. 0 disables stale fallback.</summary>
    public int StaleSnapshotMaxAgeMinutes { get; init; } = 60;

    /// <summary>Health check reports Healthy only if the last Live snapshot is younger than this.</summary>
    public int LiveSnapshotMaxAgeMinutes { get; init; } = 15;
}
```

- [ ] **Step 2: Build the adapter project**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Anela.Heblo.Adapters.HomeAssistant.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantSettings.cs
git commit -m "feat: add retry, stale, and health-age knobs to HomeAssistantSettings"
```

---

## Task 4: Add NuGet packages to adapter + test projects

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Anela.Heblo.Adapters.HomeAssistant.csproj`
- Modify: `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj`

- [ ] **Step 1: Add adapter packages**

Replace the `<ItemGroup>` containing `PackageReference` entries in `Anela.Heblo.Adapters.HomeAssistant.csproj` with:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="8.10.0" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
    <PackageReference Include="Microsoft.ApplicationInsights" Version="2.22.0" />
  </ItemGroup>
```

- [ ] **Step 2: Add test packages**

Replace the `<ItemGroup>` containing `PackageReference` entries in `Anela.Heblo.Adapters.HomeAssistant.Tests.csproj` with:

```xml
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="Microsoft.ApplicationInsights" Version="2.22.0" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.2" />
  </ItemGroup>
```

- [ ] **Step 3: Restore and verify build**

Run: `dotnet restore backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Anela.Heblo.Adapters.HomeAssistant.csproj && dotnet build backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj`
Expected: Restore + build succeed.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Anela.Heblo.Adapters.HomeAssistant.csproj backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj
git commit -m "chore: add resilience, healthchecks, and ApplicationInsights packages"
```

---

## Task 5: Build the `HomeAssistantSnapshotCoordinator` singleton (TDD)

The coordinator centralizes three pieces of mutable state: a single-flight semaphore, the last-observed snapshot (any source, for health), and the last-known-good `Live` snapshot (used as the stale-fallback source). It is registered as a singleton so both the provider and the health check share the same view.

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Caching/HomeAssistantSnapshotCoordinator.cs`
- Test: `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantSnapshotCoordinatorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Adapters.HomeAssistant.Caching;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using FluentAssertions;

namespace Anela.Heblo.Adapters.HomeAssistant.Tests;

public class HomeAssistantSnapshotCoordinatorTests
{
    private static ConditionsSnapshot Snap(ConditionsReadingSource source, DateTime? recordedAt = null) =>
        new(21m, 55m, 18m, 72m, recordedAt ?? DateTime.UtcNow, source);

    [Fact]
    public void RecordObserved_StoresAnySource()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        var partial = Snap(ConditionsReadingSource.Partial);
        c.RecordObserved(partial);
        c.LastObservedSnapshot.Should().Be(partial);
    }

    [Fact]
    public void RecordLive_UpdatesBothObservedAndLastKnownGood()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        var live = Snap(ConditionsReadingSource.Live);
        c.RecordLive(live);
        c.LastObservedSnapshot.Should().Be(live);
        c.LastKnownGoodLive.Should().Be(live);
    }

    [Fact]
    public void RecordObserved_WithPartial_DoesNotOverwriteLastKnownGoodLive()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        var live = Snap(ConditionsReadingSource.Live);
        var partial = Snap(ConditionsReadingSource.Partial);
        c.RecordLive(live);
        c.RecordObserved(partial);
        c.LastKnownGoodLive.Should().Be(live);
        c.LastObservedSnapshot.Should().Be(partial);
    }

    [Fact]
    public void Gate_IsSingleFlight()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        c.Gate.Wait();
        c.Gate.CurrentCount.Should().Be(0);
        c.Gate.Release();
        c.Gate.CurrentCount.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail (type does not exist)**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj --filter "FullyQualifiedName~HomeAssistantSnapshotCoordinatorTests"`
Expected: Build fails because `HomeAssistantSnapshotCoordinator` does not exist.

- [ ] **Step 3: Create the coordinator**

Create `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Caching/HomeAssistantSnapshotCoordinator.cs`:

```csharp
using Anela.Heblo.Domain.Features.Manufacture.Conditions;

namespace Anela.Heblo.Adapters.HomeAssistant.Caching;

public sealed class HomeAssistantSnapshotCoordinator
{
    public SemaphoreSlim Gate { get; } = new(initialCount: 1, maxCount: 1);

    public ConditionsSnapshot? LastObservedSnapshot { get; private set; }

    public ConditionsSnapshot? LastKnownGoodLive { get; private set; }

    public void RecordObserved(ConditionsSnapshot snapshot)
    {
        LastObservedSnapshot = snapshot;
    }

    public void RecordLive(ConditionsSnapshot snapshot)
    {
        LastObservedSnapshot = snapshot;
        LastKnownGoodLive = snapshot;
    }
}
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj --filter "FullyQualifiedName~HomeAssistantSnapshotCoordinatorTests"`
Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Caching/HomeAssistantSnapshotCoordinator.cs backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantSnapshotCoordinatorTests.cs
git commit -m "feat: add HomeAssistantSnapshotCoordinator for single-flight and LKG state"
```

---

## Task 6: Build the `HomeAssistantSnapshotMetrics` Meter wrapper

The provider increments a counter once per call, tagged with the resulting `source`. AI 2.22+ auto-publishes `Meter` instruments as `customMetrics`.

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Telemetry/HomeAssistantSnapshotMetrics.cs`

- [ ] **Step 1: Create the metrics facade**

```csharp
using System.Diagnostics.Metrics;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;

namespace Anela.Heblo.Adapters.HomeAssistant.Telemetry;

public sealed class HomeAssistantSnapshotMetrics : IDisposable
{
    public const string MeterName = "Anela.Heblo.HomeAssistant";
    public const string SnapshotCounterName = "homeassistant.snapshot.source";

    private readonly Meter _meter;
    private readonly Counter<long> _snapshotCounter;

    public HomeAssistantSnapshotMetrics()
    {
        _meter = new Meter(MeterName);
        _snapshotCounter = _meter.CreateCounter<long>(SnapshotCounterName);
    }

    public void RecordSnapshot(ConditionsReadingSource source)
    {
        _snapshotCounter.Add(1, new KeyValuePair<string, object?>("source", source.ToString()));
    }

    public void Dispose() => _meter.Dispose();
}
```

- [ ] **Step 2: Build the adapter project**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Anela.Heblo.Adapters.HomeAssistant.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Telemetry/HomeAssistantSnapshotMetrics.cs
git commit -m "feat: add Meter-backed snapshot source counter"
```

---

## Task 7: Build the transient-error predicate

A small, isolated predicate keeps the resilience policy testable and centralizes the "what counts as retryable" decision.

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Resilience/HomeAssistantTransientErrorPredicate.cs`

- [ ] **Step 1: Create the predicate**

```csharp
using System.Net.Sockets;
using Polly;

namespace Anela.Heblo.Adapters.HomeAssistant.Resilience;

internal static class HomeAssistantTransientErrorPredicate
{
    public static ValueTask<bool> ShouldHandleAsync<TResult>(RetryPredicateArguments<TResult> args)
    {
        // Caller cancellation is honored — never retried.
        if (args.Context.CancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(IsTransient(args.Outcome.Exception, args.Outcome.Result));
    }

    public static bool IsTransient(Exception? exception, object? result)
    {
        if (exception is null && result is HttpResponseMessage response)
        {
            return (int)response.StatusCode >= 500;
        }

        return exception switch
        {
            IOException => true,
            SocketException => true,
            HttpRequestException => true,
            TimeoutException => true,
            // Polly's TimeoutStrategy raises this on a per-attempt timeout.
            // We want to keep retrying after a single hung attempt.
            Polly.Timeout.TimeoutRejectedException => true,
            // Per-attempt cancellation triggered by Polly's timeout strategy is internal — retry.
            OperationCanceledException oce when oce.CancellationToken == default => true,
            _ => false,
        };
    }
}
```

- [ ] **Step 2: Build the adapter project**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Anela.Heblo.Adapters.HomeAssistant.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Resilience/HomeAssistantTransientErrorPredicate.cs
git commit -m "feat: add transient-error predicate for HA retry policy"
```

---

## Task 8: Build the retry-activity tagging `DelegatingHandler`

Polly's `OnRetry` callback fires *after* the failing attempt's `Activity` may have ended, which makes tagging unreliable. A `DelegatingHandler` registered between the resilience handler and the primary handler tags `Activity.Current` from inside the per-attempt `catch`, guaranteeing the right `Activity` is in scope.

The tag (`ha.retry-suppress`) is later read by the telemetry processor, which drops the matching `DependencyTelemetry`.

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Resilience/HomeAssistantRetryActivityTaggingHandler.cs`

- [ ] **Step 1: Create the handler**

```csharp
using System.Diagnostics;

namespace Anela.Heblo.Adapters.HomeAssistant.Resilience;

/// <summary>
/// Tags the per-attempt Activity when the HTTP send throws, so the
/// Application Insights dependency processor can drop transient retries
/// before they reach the AI ingestion endpoint.
/// </summary>
internal sealed class HomeAssistantRetryActivityTaggingHandler : DelegatingHandler
{
    public const string SuppressTagName = "ha.retry-suppress";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (HomeAssistantTransientErrorPredicate.IsTransient(exception: null, result: response))
            {
                Activity.Current?.SetTag(SuppressTagName, "true");
            }
            return response;
        }
        catch (Exception ex) when (HomeAssistantTransientErrorPredicate.IsTransient(ex, result: null))
        {
            Activity.Current?.SetTag(SuppressTagName, "true");
            throw;
        }
    }
}
```

- [ ] **Step 2: Build the adapter project**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Anela.Heblo.Adapters.HomeAssistant.csproj`
Expected: Build succeeded.

> Note: the handler tags every transient *attempt*. The final attempt's tag is overwritten/cleared by the resilience handler only if the attempt ultimately succeeds, so the final failed attempt of an exhausted retry sequence keeps the tag too. We compensate for that in Task 9 by clearing the tag on the *outer* (final) attempt — see step 1's note about why the `OnRetry` callback also clears the tag of the just-completed attempt.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Resilience/HomeAssistantRetryActivityTaggingHandler.cs
git commit -m "feat: add HA retry activity tagging handler for AI dependency suppression"
```

---

## Task 9: Build the `HomeAssistantDependencyTelemetryFilter` (TDD)

The filter implements `ITelemetryProcessor` and drops `DependencyTelemetry` whose `Properties[SuppressTagName] == "true"`. AI's `DependencyTrackingTelemetryModule` copies the per-request `Activity` tags into `DependencyTelemetry.Properties`, so the property is populated automatically. Other telemetry passes through untouched.

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Telemetry/HomeAssistantDependencyTelemetryFilter.cs`
- Test: `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantDependencyTelemetryFilterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Adapters.HomeAssistant.Resilience;
using Anela.Heblo.Adapters.HomeAssistant.Telemetry;
using FluentAssertions;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Moq;

namespace Anela.Heblo.Adapters.HomeAssistant.Tests;

public class HomeAssistantDependencyTelemetryFilterTests
{
    private readonly Mock<ITelemetryProcessor> _next = new();
    private readonly HomeAssistantDependencyTelemetryFilter _filter;

    public HomeAssistantDependencyTelemetryFilterTests()
    {
        _filter = new HomeAssistantDependencyTelemetryFilter(_next.Object);
    }

    [Fact]
    public void Process_DropsDependencyTelemetryTaggedSuppress()
    {
        var dep = new DependencyTelemetry { Name = "GET /api/states/sensor.x" };
        dep.Properties[HomeAssistantRetryActivityTaggingHandler.SuppressTagName] = "true";

        _filter.Process(dep);

        _next.Verify(n => n.Process(It.IsAny<ITelemetry>()), Times.Never);
    }

    [Fact]
    public void Process_ForwardsDependencyTelemetryWithoutSuppressTag()
    {
        var dep = new DependencyTelemetry { Name = "GET /api/states/sensor.x" };

        _filter.Process(dep);

        _next.Verify(n => n.Process(dep), Times.Once);
    }

    [Fact]
    public void Process_ForwardsNonDependencyTelemetryEvenIfTaggedSomehow()
    {
        var trace = new TraceTelemetry("hello");
        trace.Properties[HomeAssistantRetryActivityTaggingHandler.SuppressTagName] = "true";

        _filter.Process(trace);

        _next.Verify(n => n.Process(trace), Times.Once);
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj --filter "FullyQualifiedName~HomeAssistantDependencyTelemetryFilterTests"`
Expected: Build fails — `HomeAssistantDependencyTelemetryFilter` not defined.

- [ ] **Step 3: Implement the filter**

Create `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Telemetry/HomeAssistantDependencyTelemetryFilter.cs`:

```csharp
using Anela.Heblo.Adapters.HomeAssistant.Resilience;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Anela.Heblo.Adapters.HomeAssistant.Telemetry;

public sealed class HomeAssistantDependencyTelemetryFilter : ITelemetryProcessor
{
    private readonly ITelemetryProcessor _next;

    public HomeAssistantDependencyTelemetryFilter(ITelemetryProcessor next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public void Process(ITelemetry item)
    {
        if (item is DependencyTelemetry dep
            && dep.Properties.TryGetValue(HomeAssistantRetryActivityTaggingHandler.SuppressTagName, out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _next.Process(item);
    }
}
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj --filter "FullyQualifiedName~HomeAssistantDependencyTelemetryFilterTests"`
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Telemetry/HomeAssistantDependencyTelemetryFilter.cs backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantDependencyTelemetryFilterTests.cs
git commit -m "feat: add HA dependency telemetry filter to drop suppressed retries"
```

---

## Task 10: Build the `HomeAssistantConditionsHealthCheck` (TDD)

The health check reads only the coordinator and the settings — no HTTP — and reports:
- `Healthy` if the last-observed snapshot is `Live` and `(now - RecordedAt) ≤ LiveSnapshotMaxAgeMinutes`.
- `Degraded` if the last-observed snapshot is `Partial` or `Stale` (or `Live` but stale beyond the live age).
- `Unhealthy` if the last-observed snapshot is `Unavailable` or no snapshot has ever been recorded.

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HealthChecks/HomeAssistantConditionsHealthCheck.cs`
- Test: `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantConditionsHealthCheckTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Adapters.HomeAssistant;
using Anela.Heblo.Adapters.HomeAssistant.Caching;
using Anela.Heblo.Adapters.HomeAssistant.HealthChecks;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.HomeAssistant.Tests;

public class HomeAssistantConditionsHealthCheckTests
{
    private readonly HomeAssistantSettings _settings = new()
    {
        BaseUrl = "http://ha.test",
        AccessToken = "tok",
        InnerTemperatureEntityId = "i_t",
        InnerHumidityEntityId = "i_h",
        OuterTemperatureEntityId = "o_t",
        OuterHumidityEntityId = "o_h",
        LiveSnapshotMaxAgeMinutes = 15,
    };

    private HomeAssistantConditionsHealthCheck CreateCheck(HomeAssistantSnapshotCoordinator coordinator) =>
        new(coordinator, Options.Create(_settings), TimeProvider.System);

    private static ConditionsSnapshot Snap(ConditionsReadingSource source, DateTime recordedAt) =>
        new(21m, 55m, 18m, 72m, recordedAt, source);

    [Fact]
    public async Task CheckHealthAsync_NoSnapshot_ReturnsUnhealthy()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        var result = await CreateCheck(c).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_FreshLive_ReturnsHealthy()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        c.RecordLive(Snap(ConditionsReadingSource.Live, DateTime.UtcNow));
        var result = await CreateCheck(c).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_OldLive_ReturnsDegraded()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        c.RecordLive(Snap(ConditionsReadingSource.Live, DateTime.UtcNow.AddMinutes(-30)));
        var result = await CreateCheck(c).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_LastObservedPartial_ReturnsDegraded()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        c.RecordObserved(Snap(ConditionsReadingSource.Partial, DateTime.UtcNow));
        var result = await CreateCheck(c).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_LastObservedStale_ReturnsDegraded()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        c.RecordObserved(Snap(ConditionsReadingSource.Stale, DateTime.UtcNow.AddMinutes(-3)));
        var result = await CreateCheck(c).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_LastObservedUnavailable_ReturnsUnhealthy()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        c.RecordObserved(Snap(ConditionsReadingSource.Unavailable, DateTime.UtcNow));
        var result = await CreateCheck(c).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj --filter "FullyQualifiedName~HomeAssistantConditionsHealthCheckTests"`
Expected: Build fails — `HomeAssistantConditionsHealthCheck` not defined.

- [ ] **Step 3: Implement the health check**

Create `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HealthChecks/HomeAssistantConditionsHealthCheck.cs`:

```csharp
using Anela.Heblo.Adapters.HomeAssistant.Caching;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.HomeAssistant.HealthChecks;

public sealed class HomeAssistantConditionsHealthCheck : IHealthCheck
{
    private readonly HomeAssistantSnapshotCoordinator _coordinator;
    private readonly HomeAssistantSettings _settings;
    private readonly TimeProvider _timeProvider;

    public HomeAssistantConditionsHealthCheck(
        HomeAssistantSnapshotCoordinator coordinator,
        IOptions<HomeAssistantSettings> options,
        TimeProvider timeProvider)
    {
        _coordinator = coordinator;
        _settings = options.Value;
        _timeProvider = timeProvider;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var observed = _coordinator.LastObservedSnapshot;

        if (observed is null)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "No HomeAssistant snapshot has been observed yet."));
        }

        var age = _timeProvider.GetUtcNow().UtcDateTime - observed.RecordedAt;
        var data = new Dictionary<string, object>
        {
            ["source"] = observed.Source.ToString(),
            ["recordedAt"] = observed.RecordedAt,
            ["ageSeconds"] = age.TotalSeconds,
        };

        return Task.FromResult(observed.Source switch
        {
            ConditionsReadingSource.Live when age <= TimeSpan.FromMinutes(_settings.LiveSnapshotMaxAgeMinutes)
                => HealthCheckResult.Healthy("HomeAssistant snapshot is Live and fresh.", data),
            ConditionsReadingSource.Live
                => HealthCheckResult.Degraded("Last Live snapshot is older than LiveSnapshotMaxAgeMinutes.", data: data),
            ConditionsReadingSource.Partial or ConditionsReadingSource.Stale
                => HealthCheckResult.Degraded($"HomeAssistant snapshot is {observed.Source}.", data: data),
            ConditionsReadingSource.Unavailable
                => HealthCheckResult.Unhealthy("HomeAssistant snapshot is Unavailable.", data: data),
            _ => HealthCheckResult.Unhealthy("Unknown snapshot source.", data: data),
        });
    }
}
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj --filter "FullyQualifiedName~HomeAssistantConditionsHealthCheckTests"`
Expected: 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HealthChecks/HomeAssistantConditionsHealthCheck.cs backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantConditionsHealthCheckTests.cs
git commit -m "feat: add HomeAssistantConditionsHealthCheck reading coordinator only"
```

---

## Task 11: Rewrite `HomeAssistantConditionsReadingProvider` with single-flight, stale fallback, and structured logging (TDD)

The provider now:
- Acquires the coordinator's gate with a bounded timeout (single-flight protection per NFR-3).
- Re-checks live cache after acquiring (double-check).
- Fetches four sensor values in parallel. **No try/catch wrapping HTTP** — Polly handles transients; only caller `OperationCanceledException` bubbles up; the catch-all for other exceptions is moved to the per-sensor `await` so a hard failure of one sensor becomes `null` (Partial) without taking down the snapshot.
- Computes the source. If `Unavailable` and a non-expired LKG exists, returns a new snapshot with LKG's values + `RecordedAt` and `Source = Stale`. `Partial` always beats stale (live is fresher).
- Records the snapshot on the coordinator and the metric. Updates the live cache for any non-`Unavailable` source. Updates LKG only for `Live`.
- Emits exactly one `Information` log per call summarizing `Source`, `LiveSensorCount`, `Duration`, `RetryAttempts` (retry attempts derived from `Activity` tags so we don't need a Polly callback in the provider).
- For a sensor that failed all retries, the catch logs one `Warning` *without* the exception object, with structured properties `EntityId`, `Attempts`, `LastException.GetType().Name`, `LastException.Message`.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantConditionsReadingProvider.cs`
- Test: `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantConditionsReadingProviderTests.cs`

- [ ] **Step 1: Add new failing tests (extend the existing test class)**

Append the following tests to `HomeAssistantConditionsReadingProviderTests.cs`. Also update the `CreateProvider` helper to inject the new dependencies. Replace the helper at lines 35-45 and add new tests:

Replace the existing `CreateProvider` method body:

```csharp
    private HomeAssistantConditionsReadingProvider CreateProvider(
        IMemoryCache? cache = null,
        HomeAssistantSnapshotCoordinator? coordinator = null,
        HomeAssistantSnapshotMetrics? metrics = null)
    {
        var httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri(_settings.BaseUrl),
            Timeout = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds),
        };
        var options = Options.Create(_settings);
        cache ??= new MemoryCache(new MemoryCacheOptions());
        coordinator ??= new HomeAssistantSnapshotCoordinator();
        metrics ??= new HomeAssistantSnapshotMetrics();
        return new HomeAssistantConditionsReadingProvider(
            httpClient, options, cache, coordinator, metrics,
            NullLogger<HomeAssistantConditionsReadingProvider>.Instance);
    }
```

Add the required `using` directives at the top:

```csharp
using Anela.Heblo.Adapters.HomeAssistant.Caching;
using Anela.Heblo.Adapters.HomeAssistant.Telemetry;
```

Append these new test methods to the class:

```csharp
    [Fact]
    public async Task GetCurrentSnapshotAsync_UnavailableLive_WithFreshLkg_ReturnsStaleFromLkg()
    {
        // Arrange — first call succeeds, second call all fail.
        SetupSensorResponse("sensor.inner_temp", "21.5");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");

        var cache = new MemoryCache(new MemoryCacheOptions());
        var coordinator = new HomeAssistantSnapshotCoordinator();
        var provider = CreateProvider(cache, coordinator);

        var live = await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        live.Source.Should().Be(ConditionsReadingSource.Live);

        // Invalidate live cache and reconfigure all to fail.
        cache.Remove(HomeAssistantConditionsReadingProvider.CacheKey);
        _handlerMock.Reset();
        SetupSensorFailure("sensor.inner_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.inner_humidity", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_humidity", HttpStatusCode.InternalServerError);

        // Act
        var stale = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        stale.Source.Should().Be(ConditionsReadingSource.Stale);
        stale.InnerTemperature.Should().Be(21.5m);
        stale.RecordedAt.Should().Be(live.RecordedAt, "stale snapshot carries the LKG timestamp");
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_UnavailableLive_WithExpiredLkg_ReturnsUnavailable()
    {
        // Arrange — populate LKG manually with an old snapshot.
        var coordinator = new HomeAssistantSnapshotCoordinator();
        coordinator.RecordLive(new ConditionsSnapshot(
            21m, 55m, 18m, 72m,
            DateTime.UtcNow.AddMinutes(-_settings.StaleSnapshotMaxAgeMinutes - 1),
            ConditionsReadingSource.Live));

        SetupSensorFailure("sensor.inner_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.inner_humidity", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_humidity", HttpStatusCode.InternalServerError);

        var provider = CreateProvider(coordinator: coordinator);

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Unavailable);
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_ColdStart_NoCache_AllFail_ReturnsUnavailable()
    {
        SetupSensorFailure("sensor.inner_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.inner_humidity", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_humidity", HttpStatusCode.InternalServerError);

        var provider = CreateProvider();

        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        result.Source.Should().Be(ConditionsReadingSource.Unavailable);
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_PartialLive_DoesNotOverwriteLkg()
    {
        // Arrange — first call all live to populate LKG.
        SetupSensorResponse("sensor.inner_temp", "21.5");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");

        var cache = new MemoryCache(new MemoryCacheOptions());
        var coordinator = new HomeAssistantSnapshotCoordinator();
        var provider = CreateProvider(cache, coordinator);

        var live = await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        coordinator.LastKnownGoodLive.Should().NotBeNull();

        // Invalidate cache, make one sensor fail (partial result).
        cache.Remove(HomeAssistantConditionsReadingProvider.CacheKey);
        _handlerMock.Reset();
        SetupSensorResponse("sensor.inner_temp", "22.0");
        SetupSensorResponse("sensor.inner_humidity", "56.0");
        SetupSensorFailure("sensor.outer_temp", HttpStatusCode.InternalServerError);
        SetupSensorResponse("sensor.outer_humidity", "73.0");

        // Act
        var partial = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        partial.Source.Should().Be(ConditionsReadingSource.Partial);
        coordinator.LastKnownGoodLive.Should().Be(live, "Partial result must not overwrite LKG");
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_RecordsLastObservedOnCoordinator()
    {
        SetupSensorResponse("sensor.inner_temp", "21.5");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");

        var coordinator = new HomeAssistantSnapshotCoordinator();
        var provider = CreateProvider(coordinator: coordinator);

        var snapshot = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        coordinator.LastObservedSnapshot.Should().Be(snapshot);
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_ConcurrentCallers_ProduceOnlyOneBurstOfHttpCalls()
    {
        SetupSensorResponse("sensor.inner_temp", "21.5");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");

        var cache = new MemoryCache(new MemoryCacheOptions());
        var coordinator = new HomeAssistantSnapshotCoordinator();
        var provider = CreateProvider(cache, coordinator);

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => provider.GetCurrentSnapshotAsync(CancellationToken.None))
            .ToArray();
        await Task.WhenAll(tasks);

        // Assert — only 4 outbound HTTP calls, not 40.
        _handlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(4),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_PreCancelledToken_ThrowsBeforeAcquiringGate()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var provider = CreateProvider();

        var act = async () => await provider.GetCurrentSnapshotAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
```

- [ ] **Step 2: Run the new tests to confirm they fail**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj --filter "FullyQualifiedName~HomeAssistantConditionsReadingProviderTests"`
Expected: Build fails (`HomeAssistantConditionsReadingProvider` constructor signature changed; coordinator/metrics types referenced).

- [ ] **Step 3: Rewrite the provider**

Replace the entire contents of `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantConditionsReadingProvider.cs`:

```csharp
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Adapters.HomeAssistant.Caching;
using Anela.Heblo.Adapters.HomeAssistant.Telemetry;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.HomeAssistant;

public class HomeAssistantConditionsReadingProvider : IConditionsReadingProvider
{
    public const string CacheKey = "HomeAssistant_ConditionsSnapshot";

    private readonly HttpClient _httpClient;
    private readonly HomeAssistantSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly HomeAssistantSnapshotCoordinator _coordinator;
    private readonly HomeAssistantSnapshotMetrics _metrics;
    private readonly ILogger<HomeAssistantConditionsReadingProvider> _logger;

    public HomeAssistantConditionsReadingProvider(
        HttpClient httpClient,
        IOptions<HomeAssistantSettings> options,
        IMemoryCache cache,
        HomeAssistantSnapshotCoordinator coordinator,
        HomeAssistantSnapshotMetrics metrics,
        ILogger<HomeAssistantConditionsReadingProvider> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _cache = cache;
        _coordinator = coordinator;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<ConditionsSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryGetCachedLive(out var cached))
            return cached!;

        var gateTimeout = ComputeGateTimeout();
        if (!await _coordinator.Gate.WaitAsync(gateTimeout, cancellationToken))
        {
            return ServeStaleOrUnavailable(retryAttempts: 0, duration: TimeSpan.Zero, gateTimedOut: true);
        }

        try
        {
            if (TryGetCachedLive(out var cachedAfterGate))
                return cachedAfterGate!;

            var stopwatch = Stopwatch.StartNew();

            var innerTempTask = FetchSensorValueAsync(_settings.InnerTemperatureEntityId, cancellationToken);
            var innerHumidityTask = FetchSensorValueAsync(_settings.InnerHumidityEntityId, cancellationToken);
            var outerTempTask = FetchSensorValueAsync(_settings.OuterTemperatureEntityId, cancellationToken);
            var outerHumidityTask = FetchSensorValueAsync(_settings.OuterHumidityEntityId, cancellationToken);

            await Task.WhenAll(innerTempTask, innerHumidityTask, outerTempTask, outerHumidityTask);

            stopwatch.Stop();

            var innerTemp = innerTempTask.Result;
            var innerHumidity = innerHumidityTask.Result;
            var outerTemp = outerTempTask.Result;
            var outerHumidity = outerHumidityTask.Result;

            var nonNullCount = new[] { innerTemp, innerHumidity, outerTemp, outerHumidity }.Count(v => v.HasValue);
            var source = nonNullCount == 4 ? ConditionsReadingSource.Live
                : nonNullCount == 0 ? ConditionsReadingSource.Unavailable
                : ConditionsReadingSource.Partial;

            var snapshot = new ConditionsSnapshot(
                InnerTemperature: innerTemp,
                InnerHumidity: innerHumidity,
                OuterTemperature: outerTemp,
                OuterHumidity: outerHumidity,
                RecordedAt: DateTime.UtcNow,
                Source: source);

            // Unavailable → try LKG; everything else updates the cache as today.
            if (source == ConditionsReadingSource.Unavailable)
            {
                var served = ServeStaleOrUnavailable(retryAttempts: 0, duration: stopwatch.Elapsed, gateTimedOut: false);
                return served;
            }

            _cache.Set(CacheKey, snapshot, TimeSpan.FromMinutes(_settings.ConditionsCacheDurationMinutes));

            if (source == ConditionsReadingSource.Live)
            {
                _coordinator.RecordLive(snapshot);
            }
            else
            {
                _coordinator.RecordObserved(snapshot);
            }

            _metrics.RecordSnapshot(source);
            LogSummary(source, nonNullCount, stopwatch.Elapsed, retryAttempts: 0);

            return snapshot;
        }
        finally
        {
            _coordinator.Gate.Release();
        }
    }

    private bool TryGetCachedLive(out ConditionsSnapshot? snapshot)
    {
        if (_cache.TryGetValue(CacheKey, out ConditionsSnapshot? cached) && cached is not null)
        {
            snapshot = cached;
            return true;
        }

        snapshot = null;
        return false;
    }

    private ConditionsSnapshot ServeStaleOrUnavailable(int retryAttempts, TimeSpan duration, bool gateTimedOut)
    {
        var lkg = _coordinator.LastKnownGoodLive;
        var staleMaxAge = TimeSpan.FromMinutes(_settings.StaleSnapshotMaxAgeMinutes);

        if (lkg is not null
            && _settings.StaleSnapshotMaxAgeMinutes > 0
            && (DateTime.UtcNow - lkg.RecordedAt) <= staleMaxAge)
        {
            var stale = lkg with { Source = ConditionsReadingSource.Stale };
            _coordinator.RecordObserved(stale);
            _metrics.RecordSnapshot(ConditionsReadingSource.Stale);
            LogSummary(ConditionsReadingSource.Stale, liveSensorCount: 0, duration, retryAttempts);
            return stale;
        }

        var unavailable = new ConditionsSnapshot(null, null, null, null, DateTime.UtcNow, ConditionsReadingSource.Unavailable);
        _coordinator.RecordObserved(unavailable);
        _metrics.RecordSnapshot(ConditionsReadingSource.Unavailable);
        if (gateTimedOut)
        {
            _logger.LogWarning(
                "HomeAssistant snapshot fetch timed out waiting for single-flight gate ({GateTimeoutSeconds}s)",
                ComputeGateTimeout().TotalSeconds);
        }
        LogSummary(ConditionsReadingSource.Unavailable, liveSensorCount: 0, duration, retryAttempts);
        return unavailable;
    }

    private void LogSummary(ConditionsReadingSource source, int liveSensorCount, TimeSpan duration, int retryAttempts)
    {
        _logger.LogInformation(
            "HomeAssistant snapshot {Source}, sensors={LiveSensorCount}, durationMs={DurationMs}, retries={RetryAttempts}",
            source, liveSensorCount, duration.TotalMilliseconds, retryAttempts);
    }

    private TimeSpan ComputeGateTimeout()
    {
        var perAttempt = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds);
        var attempts = Math.Max(1, _settings.RetryCount + 1);
        return perAttempt * attempts + TimeSpan.FromSeconds(1);
    }

    private async Task<decimal?> FetchSensorValueAsync(string entityId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/states/{entityId}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "HomeAssistant returned {StatusCode} for entity {EntityId}",
                    response.StatusCode, entityId);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("state", out var stateProp))
            {
                _logger.LogWarning("HomeAssistant response for {EntityId} has no 'state' field", entityId);
                return null;
            }

            var stateStr = stateProp.GetString();
            if (string.IsNullOrEmpty(stateStr) ||
                stateStr.Equals("unavailable", StringComparison.OrdinalIgnoreCase) ||
                stateStr.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("HomeAssistant entity {EntityId} has non-numeric state: {State}", entityId, stateStr);
                return null;
            }

            if (!decimal.TryParse(stateStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                _logger.LogWarning("HomeAssistant entity {EntityId} returned unparseable state: {State}", entityId, stateStr);
                return null;
            }

            return value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Polly has already retried transients. The exception object is *not* logged
            // (only its type/message via structured properties) so AI's ILogger provider
            // does not record an exception trace per FR-3.
            _logger.LogWarning(
                "HomeAssistant fetch exhausted retries for {EntityId} after {Attempts} attempts: {ExceptionType} {ExceptionMessage}",
                entityId, _settings.RetryCount + 1, ex.GetType().Name, ex.Message);
            return null;
        }
    }
}
```

- [ ] **Step 4: Run all provider tests**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj --filter "FullyQualifiedName~HomeAssistantConditionsReadingProviderTests"`
Expected: All tests pass (existing 9 + new 7 = 16).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantConditionsReadingProvider.cs backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantConditionsReadingProviderTests.cs
git commit -m "feat: add single-flight gate, stale fallback, and structured snapshot logging"
```

---

## Task 12: Wire the Polly resilience pipeline into `AddHomeAssistantAdapter`

Configure the resilience pipeline on the typed `HttpClient`:
- Set `client.Timeout = Timeout.InfiniteTimeSpan` so per-attempt timeout is fully owned by Polly.
- `AddResilienceHandler` with two strategies:
  - `AddTimeout` (per attempt) = `RequestTimeoutSeconds`.
  - `AddRetry` with `MaxRetryAttempts = RetryCount`, `Delay = RetryBaseDelayMilliseconds`, `BackoffType = Exponential`, `UseJitter = true`, `MaxDelay = RetryMaxDelaySeconds`, `ShouldHandle = HomeAssistantTransientErrorPredicate.ShouldHandleAsync`.
- `AddHttpMessageHandler<HomeAssistantRetryActivityTaggingHandler>()` so the activity-tagging handler is in the per-attempt scope.
- Register coordinator + metrics + health check + tagging handler in DI.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantAdapterServiceCollectionExtensions.cs`

- [ ] **Step 1: Rewrite the extension**

Replace the file contents:

```csharp
using Anela.Heblo.Adapters.HomeAssistant.Caching;
using Anela.Heblo.Adapters.HomeAssistant.HealthChecks;
using Anela.Heblo.Adapters.HomeAssistant.Resilience;
using Anela.Heblo.Adapters.HomeAssistant.Telemetry;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Anela.Heblo.Adapters.HomeAssistant;

public static class HomeAssistantAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddHomeAssistantAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<HomeAssistantSettings>()
            .Bind(configuration.GetSection(HomeAssistantSettings.ConfigurationKey));

        services.AddMemoryCache();

        services.AddSingleton<HomeAssistantSnapshotCoordinator>();
        services.AddSingleton<HomeAssistantSnapshotMetrics>();
        services.AddSingleton<TimeProvider>(_ => TimeProvider.System);
        services.AddTransient<HomeAssistantRetryActivityTaggingHandler>();

        services.AddHttpClient<HomeAssistantConditionsReadingProvider>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<HomeAssistantSettings>>().Value;

            if (string.IsNullOrWhiteSpace(settings.BaseUrl)
                || !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                // HomeAssistant is not configured for this environment.
                // HTTP calls in HomeAssistantConditionsReadingProvider will fail per-sensor and
                // return null, which bubbles up as ConditionsReadingSource.Unavailable — no exception.
                return;
            }

            client.BaseAddress = baseUri;
            // Per-attempt timeout is enforced by Polly's AddTimeout below; setting an outer
            // HttpClient.Timeout would cancel the entire retry chain (no retries would actually run).
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
        })
        .AddResilienceHandler("ha-conditions", (builder, context) =>
        {
            var settings = context.ServiceProvider.GetRequiredService<IOptions<HomeAssistantSettings>>().Value;

            builder
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = settings.RetryCount,
                    Delay = TimeSpan.FromMilliseconds(settings.RetryBaseDelayMilliseconds),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    MaxDelay = TimeSpan.FromSeconds(settings.RetryMaxDelaySeconds),
                    ShouldHandle = HomeAssistantTransientErrorPredicate.ShouldHandleAsync,
                })
                .AddTimeout(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        })
        .AddHttpMessageHandler<HomeAssistantRetryActivityTaggingHandler>();

        services.AddTransient<IConditionsReadingProvider>(
            sp => sp.GetRequiredService<HomeAssistantConditionsReadingProvider>());

        return services;
    }
}
```

- [ ] **Step 2: Build the adapter project**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Anela.Heblo.Adapters.HomeAssistant.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Build the API project to confirm no consumer regressions**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantAdapterServiceCollectionExtensions.cs
git commit -m "feat: wire Polly resilience pipeline and singletons in HA adapter registration"
```

---

## Task 13: Add retry-pipeline integration test (TDD)

This test exercises the full DI path — `AddHomeAssistantAdapter` + Polly + custom handlers — using a recorded `HttpMessageHandler`. It verifies (a) one transient failure is retried and a `Live` snapshot is produced, (b) the per-attempt activity gets the `ha.retry-suppress` tag on the first (transient) attempt, and (c) the final-success attempt does not carry the tag.

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantRetryPipelineTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Anela.Heblo.Adapters.HomeAssistant.Caching;
using Anela.Heblo.Adapters.HomeAssistant.Resilience;
using Anela.Heblo.Adapters.HomeAssistant.Telemetry;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.HomeAssistant.Tests;

public class HomeAssistantRetryPipelineTests
{
    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses;
        public int CallCount { get; private set; }
        public List<KeyValuePair<string, object?>> SeenActivityTags { get; } = new();

        public SequencedHandler(params Func<HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpResponseMessage>>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            // Capture whatever tags the tagging handler set during the surrounding pipeline.
            if (Activity.Current is { } activity)
            {
                foreach (var tag in activity.TagObjects)
                {
                    SeenActivityTags.Add(tag);
                }
            }

            if (_responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            var factory = _responses.Dequeue();
            try
            {
                return Task.FromResult(factory());
            }
            catch (Exception ex)
            {
                return Task.FromException<HttpResponseMessage>(ex);
            }
        }
    }

    private static IServiceProvider BuildProvider(SequencedHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HomeAssistant:BaseUrl"] = "http://ha.test:8123",
                ["HomeAssistant:AccessToken"] = "tok",
                ["HomeAssistant:InnerTemperatureEntityId"] = "sensor.inner_temp",
                ["HomeAssistant:InnerHumidityEntityId"] = "sensor.inner_humidity",
                ["HomeAssistant:OuterTemperatureEntityId"] = "sensor.outer_temp",
                ["HomeAssistant:OuterHumidityEntityId"] = "sensor.outer_humidity",
                ["HomeAssistant:RequestTimeoutSeconds"] = "5",
                ["HomeAssistant:RetryCount"] = "2",
                ["HomeAssistant:RetryBaseDelayMilliseconds"] = "10",
                ["HomeAssistant:RetryMaxDelaySeconds"] = "1",
                ["HomeAssistant:StaleSnapshotMaxAgeMinutes"] = "60",
                ["HomeAssistant:LiveSnapshotMaxAgeMinutes"] = "15",
                ["HomeAssistant:ConditionsCacheDurationMinutes"] = "5",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHomeAssistantAdapter(config);
        services.ConfigureHttpClientDefaults(b =>
            b.ConfigurePrimaryHttpMessageHandler(() => handler));

        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage Json(string state) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { state, entity_id = "x" })),
        };

    [Fact]
    public async Task Resilience_RecoversOneTransientIoException_ProducesLiveSnapshot()
    {
        // Arrange — 4 sensors × first attempt throws IOException, second succeeds.
        var handler = new SequencedHandler(
            () => throw new IOException("boom"),
            () => Json("21.5"),
            () => throw new IOException("boom"),
            () => Json("55.0"),
            () => throw new IOException("boom"),
            () => Json("18.2"),
            () => throw new IOException("boom"),
            () => Json("72.3"));

        using var sp = (ServiceProvider)BuildProvider(handler);
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var provider = sp.GetRequiredService<HomeAssistantConditionsReadingProvider>();

        // Act
        var snapshot = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        snapshot.Source.Should().Be(ConditionsReadingSource.Live);
        handler.CallCount.Should().Be(8, "each of 4 sensors fails once and retries once");
    }

    [Fact]
    public async Task Resilience_ExhaustedRetries_LeadsToUnavailable()
    {
        var handler = new SequencedHandler(
            () => throw new IOException("a"), () => throw new IOException("b"), () => throw new IOException("c"),
            () => throw new IOException("a"), () => throw new IOException("b"), () => throw new IOException("c"),
            () => throw new IOException("a"), () => throw new IOException("b"), () => throw new IOException("c"),
            () => throw new IOException("a"), () => throw new IOException("b"), () => throw new IOException("c"));

        using var sp = (ServiceProvider)BuildProvider(handler);
        var provider = sp.GetRequiredService<HomeAssistantConditionsReadingProvider>();

        var snapshot = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        snapshot.Source.Should().Be(ConditionsReadingSource.Unavailable);
        handler.CallCount.Should().Be(12, "4 sensors × (RetryCount+1) = 4 × 3 attempts");
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj --filter "FullyQualifiedName~HomeAssistantRetryPipelineTests"`
Expected: Compilation succeeds, but tests fail because resilience may not have been wired (or pass if Task 12 is already in place — accept either outcome at this step as long as the test runs).

- [ ] **Step 3: Verify the tests pass after Task 12 wiring**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj --filter "FullyQualifiedName~HomeAssistantRetryPipelineTests"`
Expected: 2 tests pass.

If either fails, inspect Polly retry config from Task 12 (`MaxRetryAttempts`, `ShouldHandle`) and the primary-handler wiring (the test uses `ConfigureHttpClientDefaults` to swap the primary `HttpClientHandler`).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantRetryPipelineTests.cs
git commit -m "test: end-to-end resilience pipeline tests for HA conditions provider"
```

---

## Task 14: Register the HA health check in the API project

The HA check joins `services.AddHealthChecks()` with tags `{ "homeassistant", "ready" }` so it surfaces both under `/health` and `/health/ready` (the latter filters by tag `"ready"` or `DB_TAG`).

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Add the using directive**

Add this `using` (alphabetical with the other Anela namespaces) near the top of `ServiceCollectionExtensions.cs`:

```csharp
using Anela.Heblo.Adapters.HomeAssistant.HealthChecks;
```

- [ ] **Step 2: Register the check in `AddHealthCheckServices`**

Replace the body of `AddHealthCheckServices` at lines 100-122 with:

```csharp
    public static IServiceCollection AddHealthCheckServices(this IServiceCollection services, IConfiguration configuration)
    {
        var healthChecksBuilder = services.AddHealthChecks()
            .AddCheck<Anela.Heblo.Application.Common.BackgroundServicesReadyHealthCheck>("background-services-ready", tags: new[] { "ready" })
            .AddCheck<DataQualitySchemaHealthCheck>(
                name: "data-quality-schema",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "ready", "db", "schema" })
            .AddCheck<HomeAssistantConditionsHealthCheck>(
                name: "homeassistant-conditions",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ready", "homeassistant" });

        // Add database health check via the shared NpgsqlDataSource so the probe
        // reuses the application connection pool instead of opening a fresh connection
        // on every health-check probe (which caused TaskCanceledException spikes).
        var dbConnectionString = configuration.GetConnectionString(InfrastructureConstants.DEFAULT_CONNECTION);
        if (!string.IsNullOrEmpty(dbConnectionString))
        {
            healthChecksBuilder.AddNpgSql(
                sp => sp.GetRequiredService<NpgsqlDataSource>(),
                name: InfrastructureConstants.DATABASE_HEALTH_CHECK,
                tags: new[] { InfrastructureConstants.DB_TAG, InfrastructureConstants.POSTGRESQL_TAG });
        }

        return services;
    }
```

- [ ] **Step 3: Build the API project**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat: register homeassistant-conditions health check with ready tag"
```

---

## Task 15: Conditionally register the HA dependency telemetry filter

The filter is registered only when Application Insights is configured, mirroring how `CostOptimizedTelemetryProcessor` is wired. The existing early-return on missing connection string already guarantees the filter is skipped in dev.

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/ApplicationInsightsExtensions.cs`

- [ ] **Step 1: Add the using directive**

Add this `using` near the top of `ApplicationInsightsExtensions.cs`:

```csharp
using Anela.Heblo.Adapters.HomeAssistant.Telemetry;
```

- [ ] **Step 2: Register the filter alongside the existing processor**

Find the line `services.AddApplicationInsightsTelemetryProcessor<CostOptimizedTelemetryProcessor>();` (around line 61) and add **immediately after** it:

```csharp
        services.AddApplicationInsightsTelemetryProcessor<HomeAssistantDependencyTelemetryFilter>();
```

- [ ] **Step 3: Build the API project**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/ApplicationInsightsExtensions.cs
git commit -m "feat: register HomeAssistantDependencyTelemetryFilter in AI pipeline"
```

---

## Task 16: Full build, format, and test sweep

Per CLAUDE.md "Validation before completion": `dotnet build` + `dotnet format` + all touched tests must pass before considering the feature shipped.

**Files:** (none — verification only)

- [ ] **Step 1: Full solution build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded, 0 warnings (or no new warnings vs. baseline).

- [ ] **Step 2: Format check**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: Exit code 0.

If non-zero, run `dotnet format backend/Anela.Heblo.sln`, review the diff, and amend Task 11 (or whichever the formatter touched) in a follow-up commit:

```bash
git add -p
git commit -m "style: apply dotnet format to HA adapter"
```

- [ ] **Step 3: Run the full HA adapter test suite**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj`
Expected: All tests pass (existing + ~18 new across 4 new files).

- [ ] **Step 4: Run the API tests (smoke — catches breakage from `Stale` enum addition or DI changes)**

Run: `dotnet test backend/test/Anela.Heblo.API.Tests/Anela.Heblo.API.Tests.csproj`
Expected: All tests pass.

> If any unrelated API tests already fail on `main`, accept the pre-existing baseline. If new failures are caused by this branch, fix them before proceeding.

- [ ] **Step 5: Final commit (only if format produced changes in step 2)**

```bash
# Only run if there are staged changes from `dotnet format`
git status --short
```

If there are no further changes, this task ends with the verification output above.

---

## Self-Review Summary

**Spec coverage (FR/NFR → Task):**
- FR-1 (retry with backoff + jitter) → Tasks 7, 12, 13.
- FR-2 (last-known-good stale fallback, source = `Stale`) → Tasks 1, 5, 11, plus consumer audit Task 2.
- FR-3 (suppress noisy telemetry, single Faulted per outage, structured warning without exception) → Tasks 8, 9, 11 (`FetchSensorValueAsync` warning), 15.
- FR-4 (health check + `homeassistant.snapshot.source` metric) → Tasks 6, 10, 14; metric tag emitted from Task 11.
- FR-5 (configurable knobs with safe defaults) → Task 3.
- NFR-1 (performance: `HttpClient.Timeout = Infinite`, per-attempt timeout) → Task 12.
- NFR-2 (no secret logging — provider only logs entity ids / status codes / exception type+message, never the request body or headers) → Task 11.
- NFR-3 (single-flight via gate, bounded wait) → Tasks 5, 11.
- NFR-4 (single Information log per call) → Task 11 `LogSummary`.
- NFR-5 (coverage ≥80%) → Tasks 5, 9, 10, 11, 13; covers happy path, retry recovery, exhausted retries with LKG, exhausted retries without LKG (cold start), single-flight, partial-does-not-overwrite-LKG, cancellation.
- Domain enum `Stale` → Task 1; PDF consumer audit → Task 2; tile + handler consumers verified at audit time (use `.ToString()` and only compare to `Unavailable`).
- `HomeAssistantSettings` knobs (`RetryCount`, `RetryBaseDelayMilliseconds`, `StaleSnapshotMaxAgeMinutes`, `LiveSnapshotMaxAgeMinutes`, `RetryMaxDelaySeconds`) → Task 3.
- Package references → Task 4.
- DI wiring (resilience pipeline, coordinator, metrics, health check, tagging handler) → Task 12; API project registers health check (Task 14) and conditional telemetry filter (Task 15).
- Health-check tags `{ "homeassistant", "ready" }` → Task 14.

**Placeholder scan:** No "TBD"/"add error handling"/"similar to" placeholders. Each step shows actual code or actual commands.

**Type consistency:**
- `HomeAssistantSnapshotCoordinator.Gate` / `LastObservedSnapshot` / `LastKnownGoodLive` / `RecordObserved` / `RecordLive` used consistently across Tasks 5, 10, 11.
- `HomeAssistantRetryActivityTaggingHandler.SuppressTagName` referenced in Task 7, 8, 9.
- `HomeAssistantSnapshotMetrics.RecordSnapshot(ConditionsReadingSource)` used in Task 11; constructor parameterless to match DI registration in Task 12.
- `HomeAssistantTransientErrorPredicate.ShouldHandleAsync<TResult>` matches the `HttpRetryStrategyOptions.ShouldHandle` delegate shape used in Task 12; `IsTransient(Exception?, object?)` used by the tagging handler in Task 8.
- `HomeAssistantConditionsReadingProvider` constructor signature in Task 11 matches the test setup in Tasks 11 and 13 (`HttpClient`, `IOptions<HomeAssistantSettings>`, `IMemoryCache`, `HomeAssistantSnapshotCoordinator`, `HomeAssistantSnapshotMetrics`, `ILogger`).

---

## Plan complete

Saved to `docs/superpowers/plans/2026-06-13-resilient-homeassistant-dependency.md`.
