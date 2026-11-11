## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs:150` — the refresh token is a bare `CancellationTokenSource(RefreshTimeout)`. FR-2 says "linked to application shutdown if available"; `IHostApplicationLifetime.ApplicationStopping` is not linked, so a reload in flight at shutdown runs until it finishes or the 5 s timeout fires. Bounded and harmless, but linking it (`CreateLinkedTokenSource(lifetime.ApplicationStopping)` + `CancelAfter`) would match the spec wording.
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs:131` — if every waiter abandons its wait (caller cancellation) and the shared load then faults, nobody observes the task's exception and nothing is logged for that failed reload (it surfaces only as an `UnobservedTaskException` at GC). Consider catching/logging inside `LoadOverridesAsync` (and rethrowing) so a failed refresh is always visible in telemetry, which is the point of FR-4.
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs:130` — a caller arriving right after an admin write removed the cache entry joins a still-running pre-write load and that load then re-caches the pre-write table for up to 30 s. The window is narrow and is documented in `docs/processes/module-feature-flags.md` (Known quirks); a generation counter bumped by the upsert/clear handlers would close it if it ever matters.
