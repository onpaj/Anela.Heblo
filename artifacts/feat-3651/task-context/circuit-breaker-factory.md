### task: circuit-breaker-factory

**Files:** Create `Resilience/HomeAssistantCircuitBreakerOptionsFactory.cs`; create test `HomeAssistantCircuitBreakerOptionsFactoryTests.cs`. Check `InternalsVisibleTo` for the test assembly exists (predicate is internal; add to csproj only if missing).

- [ ] Write failing tests first: (1) values map from settings; (2) out-of-range values are clamped (ratio 0.01..1, throughput >= 2, durations >= 1s) and do not throw; (3) `ShouldHandle` returns true for IOException/SocketException/HttpRequestException/500, false for `BrokenCircuitException`, 200 and caller-cancelled `OperationCanceledException`.
- [ ] Implement `internal static HttpCircuitBreakerStrategyOptions Create(HomeAssistantSettings s, ILogger logger)`: `FailureRatio`, `MinimumThroughput`, `SamplingDuration`, `BreakDuration`, `ShouldHandle` via `HomeAssistantTransientErrorPredicate.IsTransient(args.Outcome.Exception, args.Outcome.Result)`, `OnOpened` (LogWarning with break duration seconds), `OnHalfOpened`, `OnClosed` (LogInformation). Never pass exception objects to the logger. Callbacks return `default(ValueTask)`.
- [ ] Run the new tests; pass.
- [ ] Commit: `feat(ha): circuit breaker options factory`.

