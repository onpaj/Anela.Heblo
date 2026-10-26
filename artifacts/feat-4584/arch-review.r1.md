# Architecture Review
- Test-only change within existing Tests project; respects Clean Architecture (no new dependencies).
- Class is `internal`, accessible through existing InternalsVisibleTo. Construct directly with `HttpClient(stubHandler)`, `MemoryCache`, `NullLogger.Instance`.
- Recommend a small recording stub `HttpMessageHandler` (queue/route-based responses, records method+URL+body) local to the new test file. Do not refactor production code (CLAUDE.md: surgical changes).
- Risk: URL matching depends on `GraphApiHelpers.EncodePath`/`GraphBaseUrl`; assert on path suffixes (`/root:/a/b:`, `/items/{id}/children`) rather than full URLs.
- No process-doc impact (no behavior change); no DTO/API/OpenAPI impact.
