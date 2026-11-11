# Architecture Review: Unit tests for PollyExecutionStrategy

## Skip Design: true
Backend test-only change; no UI.

## Architectural Fit Assessment
Fits the existing layout: tests live in `backend/test/Anela.Heblo.Tests/Persistence/Resilience/` beside `DbResiliencePipelineProviderTests`. Persistence has `InternalsVisibleTo Anela.Heblo.Tests`, though the class under test is public. No production change needed.

## Proposed Architecture
### Component Overview
Test class builds: a `ResiliencePipeline` (via `ResiliencePipelineBuilder` with `Retry` of N attempts, zero delay, handling a test exception type), a stub `IDbResiliencePipelineProvider` returning it, a real `DbResilienceMetrics` created with a test `IMeterFactory`, a `MeterListener` collecting `db.retry.*` measurements and tags, a `ListLogger`/Moq `ILogger<PollyExecutionStrategy>`, and `ExecutionStrategyDependencies`.

### Key Design Decisions
#### Decision 1: Real metrics + MeterListener rather than mocking
`DbResilienceMetrics` is sealed and non-virtual, so it cannot be mocked. Use the real class with a minimal `IMeterFactory` implementation (or `Microsoft.Extensions.Diagnostics.Testing` MetricCollector if already referenced; check the test csproj, otherwise use `MeterListener`). Check how `DbResiliencePipelineProviderTests` / `NpgsqlConnectionInterceptorTests` already do this and reuse the pattern. Do not modify production visibility.
#### Decision 2: Real retry pipeline
Use a real Polly pipeline with zero delay to exercise true attempt counting, rather than a pass-through pipeline.
#### Decision 3: ExecutionStrategyDependencies construction
The operation delegates read `_dependencies.CurrentContext.Context`. Build `ExecutionStrategyDependencies(new CurrentDbContext(ctx), ctx.GetService<IDbContextOptions>(), logger)` with a lightweight context (InMemory or SQLite provider already referenced by the test project; verify). The delegate may ignore the context argument.

## Implementation Guidance
### Directory / Module Structure
Rewrite `backend/test/Anela.Heblo.Tests/Persistence/Resilience/PollyExecutionStrategyTests.cs` only.

### Interfaces and Contracts
Cases (each for Execute and ExecuteAsync): success-first-try; success-on-second-attempt; failure-after-retries-exhausted; failure-on-first-attempt with non-retryable exception; `RetriesOnFailure` is true.
Assert metric names `db.retry.success` (tag `total_attempts`) and `db.retry.failure` (tags `exception.type`, `total_attempts`).

### Data Flow
operation -> Polly pipeline (retries) -> attempt counter -> metrics/log on success or catch -> rethrow.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Global MeterListener picks up other tests' metrics | Medium | Filter by the meter instance from the test IMeterFactory, dispose in test |
| Constructing ExecutionStrategyDependencies awkward | Low | Use real in-memory DbContext; verify EF constructor shape at implementation |
| Brief mismatches code (failure metric signature, existing placeholder file) | Low | Follow code, not brief |

## Specification Amendments
None.

## Prerequisites
None.
