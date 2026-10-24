# Architecture Review: CostOptimizedTelemetryProcessor tests

## Skip Design: true
Backend test-only change; no UI.

## Architectural Fit Assessment
Fits the existing pattern: three sibling tests in `backend/test/Anela.Heblo.Tests/Infrastructure/Telemetry/` test `ITelemetryProcessor` implementations with `Mock<ITelemetryProcessor>` and `_next.Verify(..., Times.Never/Once)`. The class under test lives in namespace `Anela.Heblo.API.Telemetry` (file under `Infrastructure/Telemetry/`), public, constructor takes `ITelemetryProcessor next`. The test project already references the API project.

## Proposed Architecture

### Component Overview
`CostOptimizedTelemetryProcessorTests` -> `CostOptimizedTelemetryProcessor(next: Mock<ITelemetryProcessor>)`; inputs are `RequestTelemetry`, `DependencyTelemetry`, `TraceTelemetry`, `EventTelemetry`.

### Key Design Decisions

#### Decision 1: Env var handling
**Options considered:** refactor processor to inject environment; set/restore the process env var in tests.
**Chosen approach:** set/restore `ASPNETCORE_ENVIRONMENT` in tests (constructor/IDisposable). No production change (surgical-change rule).
**Rationale:** env var is process-global and other tests (WebApp factory) may read it, so put the class in a dedicated `[Collection("EnvironmentVariables")]` with a `[CollectionDefinition(..., DisableParallelization = true)]` declared in the same file, and restore the original value in Dispose.

#### Decision 2: Characterize, don't fix
A failed request to `/api/health` is dropped today. Pin with a clearly named test; do not alter the processor.

## Implementation Guidance

### Directory / Module Structure
Create `backend/test/Anela.Heblo.Tests/Infrastructure/Telemetry/CostOptimizedTelemetryProcessorTests.cs` only.

### Interfaces and Contracts
Use `[Theory]/[InlineData]` for path, extension, severity and environment matrices. Duration set via `Duration = TimeSpan.FromMilliseconds(n)`. Request `Url = new Uri("https://x/...")`. Request defaults: `Success = true`, `ResponseCode = "200"`, Duration 50 ms for the "otherwise eligible" baseline helper.

### Data Flow
Test builds telemetry -> `Process` -> verify `_next.Process(item)` invoked once with the same instance or never.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Env var leakage across parallel tests | Medium | Non-parallel collection + restore in Dispose |
| `RequestTelemetry.Success` is nullable; null Success is not dropped by fast rule | Low | Add one test pinning null Success forwarded |
| Tests encode questionable health-failure drop | Low | Name as characterization; note in test comment |

## Specification Amendments
None.

## Prerequisites
None.
