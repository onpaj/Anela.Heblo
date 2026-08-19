# Code Review: feat-3883 — Stop built-in HTTP logging from capturing the raw Authorization bearer token

## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes

Reviewed the full feature-branch diff (`main...HEAD`, merge-base `eb877423fb517dc9e8505eaa30447464457c8db0`) against `spec.r1.md`.

The only production-code change is in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`: the line `logging.RequestHeaders.Add("Authorization");` inside `AddCrossCuttingServices`'s `services.AddHttpLogging(...)` configuration is removed and replaced with an explanatory comment. This is exactly the fix FR-1 asks for (the "simplest sufficient fix" per the spec's acceptance criteria): ASP.NET Core's built-in `HttpLoggingMiddleware` only logs the real value of headers explicitly present in `HttpLoggingOptions.RequestHeaders`; headers not listed are redacted by the framework's own default behavior. Removing the line reverts `Authorization` to that same default-safe treatment.

Verified:
- `logging.LoggingFields = HttpLoggingFields.All`, `User-Agent` request-header logging, `Content-Type` response-header logging, the `application/json` media type, and the 4096-byte body log limits are all untouched (FR-2 satisfied).
- `SuppressHealthHttpLogging` (health-check suppression) and `RequestLoggingMiddleware.IsSensitiveHeader` (`Authorization`, `Cookie`, `X-API-Key`, `X-Auth-Token`, `X-Smartsupp-Hmac`) are both unmodified by this diff and remain correct; the two logging paths now agree that `Authorization` is never logged with its real value (FR-3 satisfied) without merging the two mechanisms, matching the architecture review's Decision 2.
- Grepped the whole `backend/` tree for `RequestHeaders`/`HttpLogging`/`"Authorization"`: no other call site adds `Authorization` to the built-in `HttpLoggingMiddleware`'s allow-list, and no environment-specific branch re-enables it — the fix holds identically in Development, Staging, Test, and Production per FR-1's third acceptance criterion. The other `"Authorization"` matches in the tree are unrelated (outbound `HttpClient` calls in `SmartsuppApiClient`, an inbound header read in `E2ETestController`/`HangfireAuthenticationMiddleware`, and `RequestLoggingMiddleware`'s own already-correct deny-list).
- A new regression test, `backend/test/Anela.Heblo.Tests/API/HttpLoggingAuthorizationRedactionTests.cs`, builds the real `HttpLoggingOptions` via `AddCrossCuttingServices()` at the DI level (no host required) and asserts `Authorization` is absent from `RequestHeaders` while `User-Agent`, `Content-Type`, and `HttpLoggingFields.All` are preserved — a well-targeted guard against regression.
- `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` succeeds with 0 errors (pre-existing nullable-reference warnings and the pre-existing `AccessMatrixGen` post-build tool exit-134 warning only, both unrelated to this change and present on unmodified code).
- The rest of the diff is entirely pipeline/orchestration artifacts (`artifacts/feat-3883/**`) — no other source files are touched.

This is a minimal, surgical, one-line security fix that matches the spec, the architecture review, and the design exactly. No correctness issues found; no cleanups to suggest.
