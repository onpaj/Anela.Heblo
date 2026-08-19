**Where:** `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:144-154` (`AddCrossCuttingServices`), wired unconditionally via `app.UseHttpLogging()` at `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs:77` — no environment gate, so it runs in Development, Staging, Test, and Production alike.

**What's wrong:**
```csharp
services.AddHttpLogging(logging =>
{
    logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
    logging.RequestHeaders.Add("User-Agent");
    logging.RequestHeaders.Add("Authorization");
    ...
});
```
ASP.NET Core's `HttpLoggingMiddleware` redacts any header *not* explicitly added to `RequestHeaders`; a header that IS added is logged with its **real value**. `Authorization` carries the bearer JWT for every authenticated request (Entra ID access token in real auth, or the mock scheme's token). `LoggingExtensions.ConfigureApplicationLogging` (`LoggingExtensions.cs:9-27`) routes these logs to console stdout and, wherever App Insights is configured (Staging/Test/Production, per `docs/architecture/environments.md`), to Application Insights traces — a durable, queryable, broadly-readable store.

**Rule violated:** the project has already made the opposite call in this same part. `RequestLoggingMiddleware.IsSensitiveHeader` (`backend/src/Anela.Heblo.API/Middleware/RequestLoggingMiddleware.cs:232-245`) explicitly excludes `Authorization`, `Cookie`, `X-API-Key`, `X-Auth-Token`, and `X-Smartsupp-Hmac` from its own header logging, precisely because these are secrets. The built-in `HttpLogging` config a few lines away in the same composition root directly contradicts that established, in-repo convention — the same invariant ("don't log Authorization") encoded once correctly and once wrong, and the two have drifted apart. This is also the same class of risk the project has already flagged and fixed for App Insights leaking the App Insights connection string itself (closed #3785), now applied to user credentials rather than an app secret — and CLAUDE.md's blanket rule that secrets never leave Key Vault extends naturally to not re-emitting them into logs.

**Concrete consequence:** any live bearer token used against the API is captured verbatim in container stdout and in App Insights traces for the lifetime of that token. Anyone with read access to logs or the App Insights resource can replay the token to impersonate the authenticated user until it expires.

**Suggested direction:** drop `"Authorization"` from `logging.RequestHeaders` in `AddCrossCuttingServices` (or explicitly redact it), so both logging paths in this part agree on what is safe to emit.
