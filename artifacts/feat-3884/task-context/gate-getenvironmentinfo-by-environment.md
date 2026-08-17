### task: gate-getenvironmentinfo-by-environment

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/E2ETestController.cs:40-54`
- Create: `backend/test/Anela.Heblo.Tests/Controllers/E2ETestControllerTests.cs`

#### Goal
Close the anonymous-in-Production information-disclosure gap in `GetEnvironmentInfo` (spec.r1.md FR-1) without touching any other action in the file (FR-2), and add unit test coverage for the new guard (no existing test file covers this controller at all).

#### Context (from spec.r1.md / arch-review.r1.md / design.r1.md)
- The exact guard clause to add, copied from `CreateE2ESession` (`E2ETestController.cs:67-73`):
  ```csharp
  if (!_environment.IsEnvironment("Staging") && !_environment.IsDevelopment())
  {
      return NotFound(new { error = "E2E endpoints only available in Staging or Development environment", currentEnvironment = _environment.EnvironmentName });
  }
  ```
- `_environment` is `IWebHostEnvironment`, already injected via the controller constructor — no DI changes needed.
- `GetEnvironmentInfo`'s current signature is `ActionResult<object> GetEnvironmentInfo()` returning `Ok(new { environment, isDevelopment, isProduction, isStaging, environmentVariables })` — this return value and its shape stay unchanged for Staging/Development; only the out-of-environment path is new.
- Design.r1.md specifies the out-of-environment response is `404 NotFound` with body `{ error, currentEnvironment }` — identical literal string to the siblings, so log-scanning/tooling that may already expect this shape (from the other three actions) is unaffected.
- No `[Authorize]`/`[AllowAnonymous]` attribute changes — out of scope per spec.r1.md.
- Test pattern precedent: `backend/test/Anela.Heblo.Tests/Controllers/DiagnosticsControllerTests.cs` mocks `IWebHostEnvironment` with `Mock<IWebHostEnvironment>` + `.Setup(e => e.EnvironmentName).Returns(environmentName)` (the `IsDevelopment()`/`IsEnvironment()` extension methods read `EnvironmentName`, so mocking that one property is sufficient — no `Moq.Protected` needed). This repo's DiagnosticsController tests also assert `NotFoundResult` for the out-of-environment case and `OkObjectResult` for the in-environment case; the E2ETestController tests below follow the same shape.
- `E2ETestController`'s constructor requires 5 dependencies: `ILogger<E2ETestController>`, `IWebHostEnvironment`, `IConfiguration`, `IServicePrincipalTokenValidator`, `IE2ESessionService` (interfaces defined in `backend/src/Anela.Heblo.API/Infrastructure/Authentication/ServicePrincipalTokenValidator.cs` and `E2ESessionService.cs`). For a `GetEnvironmentInfo`-only test, mock all of them (Moq default mocks suffice — `GetEnvironmentInfo` never touches `_configuration`, `_tokenValidator`, or `_sessionService`).

#### Implementation steps

- [ ] **Step 1: Write failing tests for the new guard**

Create `backend/test/Anela.Heblo.Tests/Controllers/E2ETestControllerTests.cs`:

```csharp
using Anela.Heblo.API.Controllers;
using Anela.Heblo.API.Infrastructure.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class E2ETestControllerTests
{
    private static E2ETestController CreateController(string environmentName)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(environmentName);

        var configuration = new Mock<IConfiguration>();
        var tokenValidator = new Mock<IServicePrincipalTokenValidator>();
        var sessionService = new Mock<IE2ESessionService>();

        return new E2ETestController(
            NullLogger<E2ETestController>.Instance,
            environment.Object,
            configuration.Object,
            tokenValidator.Object,
            sessionService.Object);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Test")]
    [InlineData("QA")]
    public void GetEnvironmentInfo_OutsideStagingOrDevelopment_ShouldReturnNotFound(string environmentName)
    {
        var controller = CreateController(environmentName);

        var result = controller.GetEnvironmentInfo();

        var notFound = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var propertyNames = notFound.Value!.GetType().GetProperties().Select(p => p.Name).ToArray();
        propertyNames.Should().Contain(new[] { "error", "currentEnvironment" });

        var currentEnvironment = (string)notFound.Value!.GetType().GetProperty("currentEnvironment")!.GetValue(notFound.Value)!;
        currentEnvironment.Should().Be(environmentName);
    }

    [Fact]
    public void GetEnvironmentInfo_OutsideStagingOrDevelopment_ShouldNotLeakEnvironmentVariables()
    {
        var controller = CreateController("Production");

        var result = controller.GetEnvironmentInfo();

        var notFound = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var propertyNames = notFound.Value!.GetType().GetProperties().Select(p => p.Name).ToArray();
        propertyNames.Should().NotContain("environmentVariables");
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void GetEnvironmentInfo_InStagingOrDevelopment_ShouldReturnOkWithEnvironmentDetails(string environmentName)
    {
        var controller = CreateController(environmentName);

        var result = controller.GetEnvironmentInfo();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var value = ok.Value!;
        var environmentProperty = (string)value.GetType().GetProperty("environment")!.GetValue(value)!;
        environmentProperty.Should().Be(environmentName);
    }
}
```

- [ ] **Step 2: Run the new tests to confirm they fail against current code**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~E2ETestControllerTests"`
Expected: the three `GetEnvironmentInfo_OutsideStagingOrDevelopment_*` test cases (5 total, from the two `[Theory]`/`[Fact]` methods covering "Production"/"Test"/"QA") FAIL — current code always returns `OkObjectResult`, never `NotFoundObjectResult`. The `GetEnvironmentInfo_InStagingOrDevelopment_ShouldReturnOkWithEnvironmentDetails` cases already PASS (no regression expected there).

- [ ] **Step 3: Add the environment guard to `GetEnvironmentInfo`**

In `backend/src/Anela.Heblo.API/Controllers/E2ETestController.cs`, replace the method body (currently lines 40-54):

```csharp
    /// <summary>
    /// Environment Info Endpoint - For debugging deployment environment
    /// </summary>
    [HttpGet("env-info")]
    public ActionResult<object> GetEnvironmentInfo()
    {
        // CRITICAL SECURITY: Only allow in Staging or Development environment
        if (!_environment.IsEnvironment("Staging") && !_environment.IsDevelopment())
        {
            return NotFound(new { error = "E2E endpoints only available in Staging or Development environment", currentEnvironment = _environment.EnvironmentName });
        }

        return Ok(new
        {
            environment = _environment.EnvironmentName,
            isDevelopment = _environment.IsDevelopment(),
            isProduction = _environment.IsProduction(),
            isStaging = _environment.IsEnvironment("Staging"),
            environmentVariables = new
            {
                ASPNETCORE_ENVIRONMENT = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            }
        });
    }
```

Do not modify `CreateE2ESession`, `GetAuthStatus`, `GetE2EApp`, `GetE2EAppHtml`, the constructor, the class doc-comment, or any `using` directives — all already present and correct.

- [ ] **Step 4: Run the tests again to confirm they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~E2ETestControllerTests"`
Expected: all test cases PASS (6 total: 3 `[InlineData]` cases in `GetEnvironmentInfo_OutsideStagingOrDevelopment_ShouldReturnNotFound`, 1 `GetEnvironmentInfo_OutsideStagingOrDevelopment_ShouldNotLeakEnvironmentVariables`, 2 `[InlineData]` cases in `GetEnvironmentInfo_InStagingOrDevelopment_ShouldReturnOkWithEnvironmentDetails`).

- [ ] **Step 5: Run the full backend test suite to check for regressions**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: all tests PASS, no other test touches `E2ETestController` today so no other suite should be affected.

- [ ] **Step 6: Build and format the whole backend solution**

Run: `cd backend && dotnet build` then `dotnet format`
Expected: build succeeds with no new warnings/errors; `dotnet format` makes no unexpected changes.

- [ ] **Step 7: Commit**

```bash
cd backend
git add src/Anela.Heblo.API/Controllers/E2ETestController.cs \
        test/Anela.Heblo.Tests/Controllers/E2ETestControllerTests.cs
git commit -m "fix(e2e): gate GetEnvironmentInfo behind Staging/Development environment check"
```

#### Acceptance criteria
- All acceptance criteria in `spec.r1.md` FR-1 and FR-2 are met and covered by the tests above.
- `GET /api/E2ETest/env-info` returns `404 NotFound` with `{ error, currentEnvironment }` outside Staging/Development, and unchanged `200 OK` behavior inside Staging/Development.
- `dotnet build` and `dotnet format` succeed with no new warnings.
- The diff touches only `GetEnvironmentInfo` in `E2ETestController.cs` plus the new test file — `CreateE2ESession`, `GetAuthStatus`, and `GetE2EApp` are byte-for-byte unchanged.
- No public interface or DI registration changed.
