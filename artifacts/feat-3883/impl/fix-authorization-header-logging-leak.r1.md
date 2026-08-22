# Implementation: fix-authorization-header-logging-leak

## What was implemented
Removed `logging.RequestHeaders.Add("Authorization");` from the built-in ASP.NET Core `HttpLoggingMiddleware` configuration in `AddCrossCuttingServices`. ASP.NET Core's `HttpLoggingMiddleware` only logs the real value of headers explicitly listed in `HttpLoggingOptions.RequestHeaders`; any header not listed is redacted by the framework's own default behavior. By no longer adding `Authorization` to that allow-list, the middleware stops emitting the live bearer token (Entra ID access token or mock-auth token) to any log sink (console stdout, Application Insights) in every environment (Development, Staging, Test, Production — no environment gate existed or was added). A comment was added at the removal site explaining why `Authorization` must never be re-added, referencing `RequestLoggingMiddleware.IsSensitiveHeader`, which already enforces the equivalent policy for the project's own custom request-logging middleware, and issue #3883.

Added a regression test that builds the real `HttpLoggingOptions` via `AddCrossCuttingServices()` (no running host required, since the property under test — the request-header allow-list — is fully determined at DI-registration time) and asserts `Authorization` is absent while `User-Agent`, `Content-Type`, and `HttpLoggingFields.All` remain unchanged.

## Files created/modified
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — removed `logging.RequestHeaders.Add("Authorization");` from `AddCrossCuttingServices`, added an explanatory comment in its place.
- `backend/test/Anela.Heblo.Tests/API/HttpLoggingAuthorizationRedactionTests.cs` (new) — regression guard with 4 tests.

## Tests
- `HttpLoggingAuthorizationRedactionTests.RequestHeaders_DoesNotIncludeAuthorization` — fails on the pre-fix code (Authorization was in the allow-list), passes after the fix.
- `HttpLoggingAuthorizationRedactionTests.RequestHeaders_StillIncludesUserAgent` — guards that User-Agent logging is preserved.
- `HttpLoggingAuthorizationRedactionTests.ResponseHeaders_StillIncludesContentType` — guards that Content-Type response logging is preserved.
- `HttpLoggingAuthorizationRedactionTests.LoggingFields_StillSetToAll` — guards that `HttpLoggingFields.All` is unchanged (fix does not narrow overall logging scope).

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HttpLoggingAuthorizationRedactionTests"
# Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4

dotnet build Anela.Heblo.sln
# Build succeeded. 0 Error(s)

dotnet test Anela.Heblo.sln
```

## Notes
- `dotnet build Anela.Heblo.sln` succeeds with 0 errors (13 pre-existing nullable-reference warnings unrelated to this change, plus a pre-existing, unrelated `AccessMatrixGen` post-build tool crash that MSBuild treats as a warning (`MSB3073`, exit code 134) and does not fail the build — this reproduces identically on unmodified code and is out of scope for this fix).
- `dotnet test Anela.Heblo.sln` (full solution) shows 187 pre-existing failures, all confirmed unrelated to this change:
  - `Anela.Heblo.Adapters.Flexi.Tests` (72 failed) and `Anela.Heblo.Adapters.Shoptet.Tests` (13 failed) — integration tests against live external Flexi/Shoptet APIs, which require credentials/network access not available in this sandbox (consistent with `docs/integrations/shoptet-api.md`: "No sandbox — every call hits a live store").
  - `Anela.Heblo.Tests` (102 failed) — all in `*IntegrationTests` / `*SqlShapeTests` classes under `Persistence`, `Features.Leaflet`, `Features.Bank`, `Features.Catalog`, `Features.Logistics`, `Features.MeetingTasks`, `Features.Photobank`, `Features.Purchase`, `Features.Invoices`, `Article.Persistence`, and `KnowledgeBase.Integration` — all fail with `System.ArgumentException: Docker is either not running or misconfigured` (Testcontainers/PostgreSQL) or equivalent live-dependency errors. None reference `ServiceCollectionExtensions`, HTTP logging, or `Authorization`.
  - No failure anywhere in the run relates to `HttpLoggingAuthorizationRedactionTests` or the changed file.
- This is a minimal, surgical change: one line removed, one explanatory comment added, one new self-contained unit test file. No other files touched.

## PR Summary
Removed `Authorization` from the built-in ASP.NET Core `HttpLoggingMiddleware`'s request-header allow-list in `AddCrossCuttingServices`, so the middleware no longer logs the raw bearer token to stdout/Application Insights in any environment. Added a DI-level regression test that fails on the old code and passes on the fix, without needing a running host.

### Changes
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — removed `logging.RequestHeaders.Add("Authorization");`, added explanatory comment
- `backend/test/Anela.Heblo.Tests/API/HttpLoggingAuthorizationRedactionTests.cs` — new regression test class (4 tests)

## Status
DONE
