### task: wire-pipeline

**Files:** Modify `HomeAssistantAdapterServiceCollectionExtensions.cs`.

- [ ] In the `ha-conditions` callback, after `.AddRetry(...)` and before `.AddTimeout(...)`, add `if (settings.CircuitBreakerEnabled)` inserting `AddCircuitBreaker(HomeAssistantCircuitBreakerOptionsFactory.Create(settings, logger))` where logger = `context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Anela.Heblo.Adapters.HomeAssistant.CircuitBreaker")`. Restructure the fluent chain into statements (`builder.AddRetry(...); if (...) builder.AddCircuitBreaker(...); builder.AddTimeout(...);`). Update the comment above to "retry + circuit breaker + per-attempt timeout".
- [ ] Confirm order is retry (outer) -> breaker -> timeout so timeouts count and open circuit is not retried.
- [ ] Run existing `HomeAssistantRetryPipelineTests`, `HomeAssistantConditionsReadingProviderTests`: all still pass (call-count assertions of 8 / >4 stay under min throughput 8 + ratio). If one trips the breaker, set `HomeAssistant:CircuitBreakerEnabled=false` in that test's config only.
- [ ] Commit: `feat(ha): wire circuit breaker into resilience pipeline`.

