Design document written to `artifacts/feat-flexibee-skladovy-pohyb-polozka-returns-/design.r1.md`.

It covers:

**Component Design** — the modified `FlexiManufactureHistoryClient` with the new `_pipeline` instance field, the exact catch hierarchy (new bare `HttpRequestException` catch for non-transient errors sits between the 503-typed catch and the existing cancellation catches), the constructor-built pipeline pseudocode, the `.csproj` package addition, and the 5-test matrix for `FlexiManufactureHistoryClientTests`.

**Data Schemas** — the Polly pipeline parameter table (MaxRetryAttempts=2, 200ms exponential, jitter, predicate scoped to `{502, 503, 504}`), and the structured log property contracts for each catch branch (distinct Warning/Error severity signals for ops). No new DTOs, API contracts, migrations, or config keys.