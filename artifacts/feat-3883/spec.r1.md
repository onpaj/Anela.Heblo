# Specification: Stop ASP.NET Core built-in HTTP logging from capturing the raw Authorization bearer token

## Summary
`AddCrossCuttingServices` in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` configures ASP.NET Core's built-in `HttpLoggingMiddleware` to explicitly capture the `Authorization` request header, which the middleware then logs with its real value (unlisted headers are redacted by default; listed ones are not). Because `app.UseHttpLogging()` is wired unconditionally with no environment gate, and `LoggingExtensions.ConfigureApplicationLogging` routes logs to console stdout and, in Staging/Test/Production, to Application Insights, this leaks live bearer tokens (Entra ID access tokens, or the mock-auth token) into durable, broadly-readable log stores in every environment. This spec defines the fix: stop the value of the `Authorization` header from ever being written to logs, while keeping the rest of the existing built-in HTTP logging (method, path, status, timing, other headers, bodies) intact.

## Background
- The project already encodes the "never log Authorization" rule once, correctly, in `RequestLoggingMiddleware.IsSensitiveHeader` (`backend/src/Anela.Heblo.API/Middleware/RequestLoggingMiddleware.cs:232-245`), which excludes `Authorization`, `Cookie`, `X-API-Key`, `X-Auth-Token`, and `X-Smartsupp-Hmac` from its own header logging.
- A few lines away in the same composition root, `AddCrossCuttingServices` configures ASP.NET Core's *built-in* `HttpLoggingMiddleware` (via `services.AddHttpLogging(...)` + `app.UseHttpLogging()`) and explicitly does the opposite: it adds `"Authorization"` to `logging.RequestHeaders`, which makes the built-in middleware log that header's real value instead of redacting it.
- Both logging paths run back-to-back on every request (`app.UseHttpLogging()` then `app.UseRequestLogging()`, per `ApplicationBuilderExtensions.cs:77-80`), so the app currently has one correct implementation and one incorrect one for the exact same invariant.
- The project already fixed the same class of problem for the App Insights connection string itself (closed issue #3785 — a secret leaking into logs/telemetry). CLAUDE.md's rule that all secrets live in Key Vault and never leave it extends naturally to not re-emitting a live bearer token (a per-user credential, arguably more sensitive than a static app secret) into logs.
- There is precedent in the same file for scoping what the built-in `HttpLoggingMiddleware` logs per-request: `SuppressHealthHttpLogging` (`ApplicationBuilderExtensions.cs:347-363`) is a registered `IHttpLoggingInterceptor` that sets `ctx.LoggingFields = HttpLoggingFields.None` for health-check paths. The same interceptor mechanism (`IHttpLoggingInterceptor.OnRequestAsync`) is capable of removing/redacting individual headers per request via `ctx.Parameters` if a redaction-based approach is preferred over an omission-based one.
- No environment gate exists on `app.UseHttpLogging()` — it is registered identically for Development, Staging, Test, and Production (per `docs/architecture/environments.md`), so the fix must hold in all of them, not just Production.

## Functional Requirements

### FR-1: Stop the built-in HTTP logging middleware from emitting the real `Authorization` header value
The `HttpLoggingMiddleware` configured in `AddCrossCuttingServices` must never write the literal value of an inbound `Authorization` request header to any log sink (console stdout, Application Insights, or any other configured `ILoggerProvider`), in any environment.

**Acceptance criteria:**
- `logging.RequestHeaders` in `AddCrossCuttingServices` no longer contains `"Authorization"` (the simplest sufficient fix per the issue's suggested direction), **or** an equivalent mechanism (e.g. an `IHttpLoggingInterceptor` that redacts the header's value before it reaches the log writer) is in place such that the header's real value never appears in emitted logs.
- A request made with `Authorization: Bearer <token>` produces a built-in HTTP-logging log entry that either omits the `Authorization` header entirely (ASP.NET Core's default behavior for headers not in `RequestHeaders`) or shows a redacted placeholder (e.g. `[Redacted]`) — never the literal bearer token.
- This holds identically in Development, Staging, Test, and Production — no environment-specific carve-out that re-enables the token in any one of them.

### FR-2: Preserve the rest of the existing built-in HTTP logging behavior
The fix must not regress the diagnostic value of the existing built-in HTTP logging for anything other than the `Authorization` header value.

**Acceptance criteria:**
- `logging.LoggingFields = HttpLoggingFields.All` is unchanged (method, path, protocol, status code, headers other than the redacted one, and request/response bodies within existing limits continue to be logged).
- `User-Agent` request header logging is unchanged.
- `Content-Type` response header logging is unchanged.
- `RequestBodyLogLimit` / `ResponseBodyLogLimit` (4096) and the `application/json` media type logging are unchanged.
- `SuppressHealthHttpLogging` (health-check path suppression) continues to function unchanged.

### FR-3: Align the two in-repo logging paths on the same sensitive-header policy
The built-in `HttpLoggingMiddleware` configuration and `RequestLoggingMiddleware.IsSensitiveHeader` must agree on what is safe to emit, removing the drift called out in the issue.

**Acceptance criteria:**
- After the fix, `Authorization` is not logged with its real value by either logging path.
- The fix does not need to unify the two mechanisms into one (they can remain separate implementations), but the *set of headers each treats as sensitive* should not silently diverge going forward — note this as a maintenance consideration for the architect/design phases to weigh (e.g. whether to share a single sensitive-header list).

## Non-Functional Requirements

### NFR-1: Security
- This is a credential-leak fix: the primary non-functional requirement is that no live bearer token (Entra ID access token or mock-auth token) is ever persisted to a log sink, in any deployed environment, after this change ships.
- No new attack surface is introduced (the fix is a removal/redaction of existing over-broad logging, not a new code path handling secrets).

### NFR-2: Backward compatibility / observability
- Existing dashboards, alerts, or manual log searches that rely on the *presence* of the `Authorization` header entry (e.g., to distinguish authenticated vs. anonymous requests) may be affected if the header is omitted entirely rather than redacted with a placeholder. This trade-off should be resolved during design (see Open Questions).
- No change to response behavior, status codes, or request handling — this is a logging-configuration-only change.

## Data Model
Not applicable — this is a logging/configuration change with no persisted data model impact.

## API / Interface Design
No public API surface changes. The only interface touched is the internal `AddCrossCuttingServices` DI registration (and possibly a new or reused `IHttpLoggingInterceptor` implementation) in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` and, if an interceptor-based approach is chosen, `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs`.

## Dependencies
- ASP.NET Core built-in `Microsoft.AspNetCore.HttpLogging` middleware (already in use — no new package).
- No dependency on `RequestLoggingMiddleware` being changed; FR-3 only asks that the two not diverge further, not that they be merged in this change.

## Out of Scope
- Rewriting or consolidating `RequestLoggingMiddleware`'s own sensitive-header handling — it is already correct.
- Adding redaction for the request/response *body* (e.g. if a token or password were ever sent in a JSON body) — this issue is scoped to the `Authorization` header specifically.
- Broader secret-scanning or log-scrubbing infrastructure beyond this one header.
- Rotating or invalidating any tokens that may have already been captured in historical logs prior to this fix (an operational follow-up, not part of this code change).

## Open Questions
None — the issue and codebase precedent (`RequestLoggingMiddleware.IsSensitiveHeader`, `SuppressHealthHttpLogging`) are specific enough to proceed. The choice between "omit the header" (simplest, matches the issue's suggested direction) and "redact with a placeholder via interceptor" (preserves a visible marker that the request was authenticated) is left to the architecture/design phases to decide, with a preference for the simpler omission-based fix unless the design phase identifies a concrete need for the placeholder.

## Status: COMPLETE
