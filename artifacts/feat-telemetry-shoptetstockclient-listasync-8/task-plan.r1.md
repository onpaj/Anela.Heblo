Plan saved to `docs/superpowers/plans/2026-06-14-shoptetstockclient-listasync-resilience.md`.

**Summary of plan (14 tasks):**

- **Tasks 1–4** — Configuration extension, package add, named `"ShoptetStockCsv"` HttpClient with `AddResilienceHandler(...)`, and the `ShoptetStockClient.ListAsync` rewrite (named client + stopwatch + structured try/catch + `RedactToken` helper).
- **Tasks 5–10** — Six TDD-style unit tests covering retry success, retry exhaustion, caller cancellation short-circuit, terminal-log redaction, per-attempt timeout abort, and `RedactToken` parametric coverage. All use a stub `HttpMessageHandler` wired into DI via `ConfigurePrimaryHttpMessageHandler` against the named client.
- **Tasks 11–12** — `ProductPairingDqtComparer` gains an `ICatalogResilienceService` dependency, wraps both `ListAsync` calls with operation names per Amendment 5; existing tests get a pass-through resilience mock and a new test verifies the wrap.
- **Task 13** — Documents §4.4 of `docs/integrations/shoptet-api.md` with the CSV resilience characteristics.
- **Task 14** — Final `dotnet build`, `dotnet format`, unit-test sweep.

Each task carries explicit file paths, complete code blocks, commands with expected output, and a single commit. Amendments 1–5 from the architecture review are folded into the relevant tasks (per-attempt timeout default = 8s; cancellation-aware retry predicate; new unit test class location; redaction contract; DQT operation names). Self-review table at the end traces every spec FR/NFR/amendment to a task.