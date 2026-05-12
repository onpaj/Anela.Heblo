Wrote `spec.md` to `artifacts/feat-flexibee-skladovy-pohyb-polozka-returns-/spec.md` covering:

- **5 functional requirements**: typed catch for transient 5xx, fallback catch for non-transient HTTP errors, 2-retry Polly policy with exponential backoff, preservation of existing behavior, and unit test coverage (5+ new tests).
- **5 NFRs**: performance budget (≤1.5 s worst-case retry latency), security (no PII in logs), observability (structured properties), backward compatibility (signature unchanged), and code quality (`dotnet format`/`build` clean).
- **Implementation sketch** mirroring the `FlexiStockClient.cs:59` pattern.
- **Out of scope** explicitly excludes circuit breakers, generalizing to other Flexi clients, graceful degradation in the handler, and config knobs.
- **5 open questions** with stated assumptions (Polly availability, retry tuning, whether timeouts should retry, whether the handler should degrade gracefully).