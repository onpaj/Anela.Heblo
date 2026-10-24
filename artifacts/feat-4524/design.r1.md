# Design: CostOptimizedTelemetryProcessor tests

## Component Design
Single xUnit class `CostOptimizedTelemetryProcessorTests : IDisposable` in namespace `Anela.Heblo.Tests.Infrastructure.Telemetry`, attributed `[Collection("EnvironmentVariables")]`, plus `[CollectionDefinition("EnvironmentVariables", DisableParallelization = true)] public class EnvironmentVariablesCollection { }` in the same file.

Fields: `Mock<ITelemetryProcessor> _next`, `CostOptimizedTelemetryProcessor _sut`, `string? _originalEnv` (captured in ctor, restored in Dispose).

Helpers: `Request(path, durationMs = 50, success = true, responseCode = "200", name = "GET /x")`, `Dependency(type, durationMs, success)`, `Trace(severity)`, `SetEnv(value)`, `AssertForwarded(item)` (Verify Once with the item), `AssertDropped()` (Verify Never).

Test groups:
- Requests: Theory over excluded paths (incl. upper-case variant) dropped; Theory over static extensions dropped; OPTIONS 200 dropped; OPTIONS non-200 forwarded; fast success dropped; fast failure forwarded; fast with null Success forwarded; 10 ms+ success forwarded; null Url forwarded; failed request on `/api/health` dropped (characterization).
- Dependencies: failed forwarded (Theory SQL/Http/other, 1 ms); SQL 30 dropped, 60 forwarded; Http 50 dropped, 150 forwarded; other type fast success forwarded.
- Traces: Theory (env, severity, expectedForwarded) covering Production/Staging/Development/unset(null) x Verbose/Information/Warning/Error/Critical.
- Other: EventTelemetry forwarded.

## Data Schemas
None.
