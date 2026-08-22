# Architecture Review: Stop built-in HTTP logging from capturing the raw Authorization bearer token

## Skip Design: true

## Architectural Fit Assessment
This is a scoped, backend-only configuration fix inside the API composition root (`backend/src/Anela.Heblo.API/Extensions/`). It touches no domain logic, no vertical-slice module, no persisted data, and no UI. It aligns cleanly with existing patterns already present in the same two files:

- `RequestLoggingMiddleware.IsSensitiveHeader` (`backend/src/Anela.Heblo.API/Middleware/RequestLoggingMiddleware.cs:232-245`) already encodes "these headers are never logged with their real value" for the project's own custom logging middleware.
- `SuppressHealthHttpLogging` (`backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs:347-363`) already demonstrates the sanctioned mechanism for altering what the *built-in* `HttpLoggingMiddleware` captures per-request: implement `IHttpLoggingInterceptor` and mutate `HttpLoggingInterceptorContext` in `OnRequestAsync`. It is already registered via `services.AddHttpLoggingInterceptor<SuppressHealthHttpLogging>()` in `AddCrossCuttingServices`.

The fix requires no new architectural pattern — it is a matter of applying the existing interceptor mechanism (or a simpler config-only change) consistently with the existing `IsSensitiveHeader` policy. No prerequisites, migrations, or infrastructure changes are needed.

## Proposed Architecture

### Component Overview
```
Request ──▶ UseForwardedHeaders ──▶ UseCors ──▶ UseHttpsRedirection
         ──▶ UseHttpLogging()  ◀── HttpLoggingMiddleware (built-in, ASP.NET Core)
                  │
                  ├─ services.AddHttpLogging(...)               [ServiceCollectionExtensions.cs]
                  │     RequestHeaders: User-Agent  (Authorization REMOVED — see Decision 1)
                  │     ResponseHeaders: Content-Type
                  │
                  └─ IHttpLoggingInterceptor chain (per-request, runs inside the middleware)
                        SuppressHealthHttpLogging   [existing — health-check paths → LoggingFields.None]
         ──▶ UseRequestLogging()  ◀── RequestLoggingMiddleware (custom, project-owned)
                  IsSensitiveHeader(...) already excludes Authorization/Cookie/etc.
         ──▶ ... rest of pipeline
```
Both logging paths remain independent, as they are today — this review does not propose merging them (see Decision 2). Only the built-in path's `Authorization` handling changes.

### Key Design Decisions

#### Decision 1: How to stop `Authorization` from being logged by the built-in middleware
**Options considered:**
1. **Remove `logging.RequestHeaders.Add("Authorization")`** from `AddCrossCuttingServices`. ASP.NET Core's `HttpLoggingMiddleware` redacts (omits the value of) any header not explicitly present in `RequestHeaders`, so the header line disappears from the value side — the middleware still logs that other headers exist, `Authorization` simply reverts to the framework's default "not enumerated" behavior, which is already how every other unlisted header (e.g. `Cookie`) is treated today.
2. **Keep `Authorization` in `RequestHeaders` but redact its value via an `IHttpLoggingInterceptor`** (extending `SuppressHealthHttpLogging` or adding a new interceptor) that rewrites the header value in `ctx.Parameters` before the middleware serializes it, e.g. to `[Redacted]`.

**Chosen approach:** Option 1 — delete the single line `logging.RequestHeaders.Add("Authorization");`.

**Rationale:** This is the minimal, surgical fix (CLAUDE.md: "surgical changes... every changed line should trace directly to the request") and it is exactly the "suggested direction" in the issue. It requires no new interceptor, no new class, and cannot regress in the future because there is no longer a `RequestHeaders.Add("Authorization")` line for someone to accidentally re-enable without noticing its implication — the absence *is* the safety property, matching how every other sensitive header (`Cookie`, `X-API-Key`, etc.) already behaves under the built-in middleware by simply never being added. Option 2 (interceptor-based redaction) is unnecessary extra surface for this problem: it only makes sense if the team specifically wants a `[Redacted]` placeholder to remain visible in logs as a signal "this request was authenticated," and nothing in the spec or issue asks for that. If that observability need arises later, it's a trivial follow-up (one more line in `SuppressHealthHttpLogging` or a sibling interceptor) — do not build it speculatively now (YAGNI).

#### Decision 2: Do not merge `RequestLoggingMiddleware.IsSensitiveHeader` with the built-in middleware's header allow-list in this change
**Options considered:**
1. Extract a single shared `static readonly string[] SensitiveHeaders` constant used by both `IsSensitiveHeader` and (implicitly, by omission) `AddCrossCuttingServices`.
2. Leave the two mechanisms independent, as today, and rely on the fix in Decision 1 plus this review's guidance to keep them from drifting again.

**Chosen approach:** Option 2 for this change; flag Option 1 as a follow-up, not a blocker.

**Rationale:** The two mechanisms are structurally different — one is an *allow-list of headers to log* (built-in `HttpLoggingMiddleware`, "add it and it's logged"), the other is a *deny-list checked against headers already being logged* (`RequestLoggingMiddleware`, "log everything except these"). A shared constant would need to be consumed inversely in each place, which adds indirection for a two-line fix and risks scope creep beyond what the issue asks for. Since the built-in middleware's `RequestHeaders` list will contain exactly one entry (`User-Agent`) after this fix, the omission of `Authorization` is self-evidently correct by inspection — a future reviewer does not need a shared constant to catch a regression, they need only to notice a new `.Add("Authorization")` (or any header) line being added carelessly, which is a code-review concern, not an architectural one. Recorded here so a future arch-review pass can revisit consolidation if more sensitive headers accumulate.

## Implementation Guidance

### Directory / Module Structure
No new files. Change is entirely within:
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — remove the one line adding `"Authorization"` to `logging.RequestHeaders` inside `AddCrossCuttingServices` (around line 149–150 per the issue's line reference).

No changes needed to `ApplicationBuilderExtensions.cs`, `RequestLoggingMiddleware.cs`, or `LoggingExtensions.cs`.

### Interfaces and Contracts
No interface or contract changes. `AddCrossCuttingServices` keeps its existing signature and return type (`IServiceCollection`). `HttpLoggingFields.All` is unchanged, so bodies, status codes, timing, method/path, and all other headers continue to be logged exactly as before — only the `Authorization` entry stops being enumerated.

### Data Flow
Unchanged except for the one header. Every authenticated request still flows through `UseHttpLogging()` then `UseRequestLogging()`; the built-in middleware will now treat `Authorization` the same way it already treats every other header the app doesn't explicitly opt in (e.g. `Cookie`) — omitted from the logged header set rather than logged with its value.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A future PR re-adds `logging.RequestHeaders.Add("Authorization")` (or another secret-bearing header) without realizing the security implication | Medium | Add a one-line code comment at the `RequestHeaders` block in `AddCrossCuttingServices` explaining why `Authorization`/`Cookie`/etc. must never be added here, mirroring the intent already documented implicitly by `RequestLoggingMiddleware.IsSensitiveHeader`'s explicit list. A regression (integration) test (see Prerequisites/verification below) also catches this automatically. |
| Loss of an "authenticated vs. anonymous" signal in built-in HTTP logs that some operator may have relied on | Low | Not a functional regression — `RequestLoggingMiddleware`'s own logs and standard auth/authz telemetry (401s, claims-based logs elsewhere) already provide this signal without needing the token value. Out of scope per spec; no action required unless a concrete need surfaces later (see Decision 1 fallback). |
| Historical logs already containing captured tokens remain in App Insights/stdout retention | Low (operational, not code) | Out of scope for this code change per spec; call out to the maintainer (solo developer) as a follow-up operational note — token rotation/log retention review is not part of this PR. |

## Specification Amendments
None. The spec's FR-1/FR-2/FR-3 and Open Questions resolution (prefer the simpler omission-based fix) match the chosen approach exactly; no changes needed to `spec.r1.md`.

## Prerequisites
None — no migrations, no config, no infrastructure changes. The only recommended (not strictly required, but strongly advised given this is a security fix) implementation step beyond the one-line removal is a regression test using the existing `HebloWebApplicationFactory` integration-test base (`backend/test/Anela.Heblo.Tests/Common/HebloWebApplicationFactory.cs`) that issues an authenticated request with a known bearer value and asserts, via a captured `ILoggerProvider`/log sink, that the literal token value never appears in emitted log output — guarding against the regression risk noted above. This is a task-planning decision (whether to add this test) left to the planner, not a hard architectural prerequisite.
