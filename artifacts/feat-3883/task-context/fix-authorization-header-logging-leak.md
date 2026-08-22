### task: fix-authorization-header-logging-leak

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:144-153`
- Test: `backend/test/Anela.Heblo.Tests/API/HttpLoggingAuthorizationRedactionTests.cs`

This task removes the single line that causes the leak and adds a regression test that fails on the current code and passes once the line is removed. The test builds a minimal `ServiceCollection`, calls the real `AddCrossCuttingServices()` extension method exactly as `Program.cs` does, and inspects the resulting `HttpLoggingOptions` — it does not stand up a full `WebApplicationFactory` host, since the property under test (which headers are in the allow-list) is fully determined at DI-registration time and does not require a running HTTP pipeline.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/API/HttpLoggingAuthorizationRedactionTests.cs`:

```csharp
using Anela.Heblo.API.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.API;

/// <summary>
/// Regression guard for the built-in HTTP logging config in AddCrossCuttingServices.
/// ASP.NET Core's HttpLoggingMiddleware logs the real value of any request header
/// explicitly added to HttpLoggingOptions.RequestHeaders, and redacts (omits the value
/// of) any header that is not listed. Authorization carries the live bearer token
/// (Entra ID access token, or the mock-auth token) for every authenticated request, so
/// it must never be added to that allow-list — see RequestLoggingMiddleware.IsSensitiveHeader
/// for the equivalent "never log this header's value" policy already enforced by the
/// project's own custom request-logging middleware.
/// </summary>
public class HttpLoggingAuthorizationRedactionTests
{
    private static HttpLoggingOptions BuildOptions()
    {
        var services = new ServiceCollection();
        services.AddCrossCuttingServices();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<HttpLoggingOptions>>().Value;
    }

    [Fact]
    public void RequestHeaders_DoesNotIncludeAuthorization()
    {
        var options = BuildOptions();

        options.RequestHeaders.Should().NotContain("Authorization",
            "the built-in HttpLoggingMiddleware logs the real value of any header explicitly " +
            "listed here; Authorization carries the live bearer token and must never be logged");
    }

    [Fact]
    public void RequestHeaders_StillIncludesUserAgent()
    {
        var options = BuildOptions();

        options.RequestHeaders.Should().Contain("User-Agent",
            "User-Agent logging is unrelated to this fix and must be preserved");
    }

    [Fact]
    public void ResponseHeaders_StillIncludesContentType()
    {
        var options = BuildOptions();

        options.ResponseHeaders.Should().Contain("Content-Type",
            "Content-Type response header logging is unrelated to this fix and must be preserved");
    }

    [Fact]
    public void LoggingFields_StillSetToAll()
    {
        var options = BuildOptions();

        options.LoggingFields.Should().Be(HttpLoggingFields.All,
            "removing Authorization from RequestHeaders must not narrow the overall logging scope");
    }
}
```

- [ ] **Step 2: Run the test to verify `RequestHeaders_DoesNotIncludeAuthorization` fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HttpLoggingAuthorizationRedactionTests"`
Expected: 4 tests run, 3 PASS, 1 FAIL — `RequestHeaders_DoesNotIncludeAuthorization` fails because the current code still calls `logging.RequestHeaders.Add("Authorization")`, so `options.RequestHeaders` contains `"Authorization"`.

- [ ] **Step 3: Remove the `Authorization` header from the built-in HTTP logging allow-list**

In `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, the current lines 143-154 read:

```csharp
        // Built-in HTTP request logging
        services.AddHttpLogging(logging =>
        {
            logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
            logging.RequestHeaders.Add("User-Agent");
            logging.RequestHeaders.Add("Authorization");
            logging.ResponseHeaders.Add("Content-Type");
            logging.MediaTypeOptions.AddText("application/json");
            logging.RequestBodyLogLimit = 4096;
            logging.ResponseBodyLogLimit = 4096;
        });
        services.AddHttpLoggingInterceptor<SuppressHealthHttpLogging>();
```

Replace with:

```csharp
        // Built-in HTTP request logging
        services.AddHttpLogging(logging =>
        {
            logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
            logging.RequestHeaders.Add("User-Agent");
            // Authorization is intentionally NOT added here: HttpLoggingMiddleware logs the
            // real value of any header explicitly listed in RequestHeaders, and Authorization
            // carries the live bearer token (Entra ID access token, or the mock-auth token) for
            // every authenticated request. Leaving it unlisted keeps it redacted by the
            // middleware's own default behavior, matching RequestLoggingMiddleware.IsSensitiveHeader
            // below, which excludes the same header for the same reason. See issue #3883.
            logging.ResponseHeaders.Add("Content-Type");
            logging.MediaTypeOptions.AddText("application/json");
            logging.RequestBodyLogLimit = 4096;
            logging.ResponseBodyLogLimit = 4096;
        });
        services.AddHttpLoggingInterceptor<SuppressHealthHttpLogging>();
```

- [ ] **Step 4: Run the test to verify all four tests pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HttpLoggingAuthorizationRedactionTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Build the full solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeds, 0 errors, 0 new warnings.

- [ ] **Step 6: Run the full backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: PASS — no regressions in any other module's tests (this change only removes one line from an allow-list consumed solely by the built-in `HttpLoggingMiddleware`; no other code reads `HttpLoggingOptions.RequestHeaders`).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs \
        backend/test/Anela.Heblo.Tests/API/HttpLoggingAuthorizationRedactionTests.cs
git commit -m "fix: stop built-in HTTP logging from capturing the raw Authorization bearer token"
```
