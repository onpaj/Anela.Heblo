Design document written to `artifacts/feat-dataquality-getruns-returns-500-relation/design.r1.md`.

Key design decisions reflected from the actual codebase (not just the arch-review assumptions):

- **Registration**: goes in `ServiceCollectionExtensions.AddHealthCheckServices()` — that's where `BackgroundServicesReadyHealthCheck` is registered, not in a hypothetical `DataQualityModule.cs` in the API layer.
- **`/health/ready`** already exists with the right predicate (`"db" || "ready"` tags), so no endpoint changes are needed — only the new check class + one `AddCheck<>` call.
- **Auth**: no `FallbackPolicy` is configured, so health endpoints are already implicitly anonymous. No `.AllowAnonymous()` call is needed.
- **Response format**: `UIResponseWriter.WriteHealthCheckUIResponse` is already wired on `/health/ready`, so the structured `data` dictionary will appear in the 503 body automatically.
- **Scope**: `DqtRuns` only for this PR; broader table coverage from `StandardizeTableNamingToPascalCase` deferred to a follow-up ticket.