# Consolidate GetCurrentUserId() into BaseApiController Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the duplicated `GetCurrentUserId()` private method from three controllers and consolidate it on `BaseApiController` with a semantically-correct `UnauthorizedAccessException`, plus an `IExceptionHandler` that maps that exception to a 401 `ProblemDetails` response.

**Architecture:** A single `protected` helper lives on `BaseApiController` (the existing place for cross-controller infrastructure). `DashboardController`, `CarrierCoolingController`, and `GiftSettingsController` lose their private copies and the now-unused `using System.Security.Claims;`. A new `UnauthorizedAccessExceptionHandler : IExceptionHandler` is registered via `AddExceptionHandler<T>()` + `AddProblemDetails()` and wired into the pipeline before `UseRouting()` so that the missing-claim path produces a `401 ProblemDetails` with no detail-message leakage to the client (exception logged server-side).

**Tech Stack:** .NET 8, ASP.NET Core 8 (`IExceptionHandler`, `ProblemDetails`), xUnit + Moq + FluentAssertions, `System.Security.Claims`.

---

## File Structure

**Modify:**
- `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs` — add `protected string GetCurrentUserId()` + `using System.Security.Claims;`.
- `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs` — delete the `private string GetCurrentUserId()` block (lines 97–105) and remove `using System.Security.Claims;` (line 1).
- `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs` — delete the `private string GetCurrentUserId()` block (lines 40–46) and remove `using System.Security.Claims;` (line 1).
- `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs` — delete the `private string GetCurrentUserId()` block (lines 40–46) and remove `using System.Security.Claims;` (line 1).
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — register `UnauthorizedAccessExceptionHandler` and `AddProblemDetails()` inside `AddCrossCuttingServices` (line 122).
- `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs` — call `app.UseExceptionHandler()` before `app.UseRouting()` (insertion point between line 86 and line 89).
- `backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs` — delete the four `GetCurrentUserId_*` tests (lines 229–333) which target moved behavior.

**Create:**
- `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandler.cs` — new `IExceptionHandler` returning 401 + `ProblemDetails`.
- `backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs` — unit tests for the consolidated `GetCurrentUserId()` via a tiny test-only derived controller.
- `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandlerTests.cs` — unit tests asserting 401 status, `ProblemDetails` body, no `detail` leakage, and server-side log on missing claim.

Each file has a single, well-scoped responsibility; the changes split naturally by file along Task boundaries.

---

## Task 1: Add `GetCurrentUserId()` to `BaseApiController`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs`

- [ ] **Step 1: Add `using System.Security.Claims;`**

Edit the using block at the top of `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs` so it becomes:

```csharp
using System.Net;
using System.Reflection;
using System.Security.Claims;
using Anela.Heblo.Application.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
```

- [ ] **Step 2: Add `GetCurrentUserId()` to the class**

Append the new method *inside* the class body (after `GetStatusCodeForError`, before the closing brace at line 73):

```csharp
    /// <summary>
    /// Returns the authenticated user's identifier from the first available claim
    /// (NameIdentifier → sub → oid). Throws <see cref="UnauthorizedAccessException"/>
    /// when none is present.
    /// </summary>
    protected string GetCurrentUserId()
        => User.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? User.FindFirst("sub")?.Value
           ?? User.FindFirst("oid")?.Value
           ?? throw new UnauthorizedAccessException("Authenticated user has no identifiable claim.");
```

- [ ] **Step 3: Build to verify no compile errors**

Run from the repo root:

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: build succeeds. (Existing callers in `DashboardController`/`CarrierCoolingController`/`GiftSettingsController` still resolve their own private method at this stage — base class addition does not conflict because C# resolves the lexically-closest member first; deletion happens in later tasks.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs
git commit -m "feat(api): add GetCurrentUserId() helper to BaseApiController"
```

---

## Task 2: Write `BaseApiController` unit tests (TDD red, then green)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs`

- [ ] **Step 1: Write the test file**

Create `backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs` with this exact content:

```csharp
using System.Security.Claims;
using Anela.Heblo.API.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class BaseApiControllerTests
{
    private sealed class TestController : BaseApiController
    {
        public string CallGetCurrentUserId() => GetCurrentUserId();
    }

    private static TestController CreateControllerWithClaims(params Claim[] claims)
    {
        var controller = new TestController();
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return controller;
    }

    [Fact]
    public void GetCurrentUserId_WhenNameIdentifierPresent_ReturnsNameIdentifier()
    {
        var controller = CreateControllerWithClaims(
            new Claim(ClaimTypes.NameIdentifier, "name-id-123"));

        var result = controller.CallGetCurrentUserId();

        result.Should().Be("name-id-123");
    }

    [Fact]
    public void GetCurrentUserId_WhenOnlySubPresent_ReturnsSub()
    {
        var controller = CreateControllerWithClaims(
            new Claim("sub", "sub-user-456"));

        var result = controller.CallGetCurrentUserId();

        result.Should().Be("sub-user-456");
    }

    [Fact]
    public void GetCurrentUserId_WhenOnlyOidPresent_ReturnsOid()
    {
        var controller = CreateControllerWithClaims(
            new Claim("oid", "oid-user-789"));

        var result = controller.CallGetCurrentUserId();

        result.Should().Be("oid-user-789");
    }

    [Fact]
    public void GetCurrentUserId_WhenMultipleClaimsPresent_PrioritizesNameIdentifier()
    {
        var controller = CreateControllerWithClaims(
            new Claim(ClaimTypes.NameIdentifier, "name-id-123"),
            new Claim("sub", "sub-user-456"),
            new Claim("oid", "oid-user-789"));

        var result = controller.CallGetCurrentUserId();

        result.Should().Be("name-id-123");
    }

    [Fact]
    public void GetCurrentUserId_WhenSubAndOidPresent_PrioritizesSub()
    {
        var controller = CreateControllerWithClaims(
            new Claim("sub", "sub-user-456"),
            new Claim("oid", "oid-user-789"));

        var result = controller.CallGetCurrentUserId();

        result.Should().Be("sub-user-456");
    }

    [Fact]
    public void GetCurrentUserId_WhenNoSupportedClaim_ThrowsUnauthorizedAccessException()
    {
        var controller = CreateControllerWithClaims(); // no claims

        var act = controller.CallGetCurrentUserId;

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("Authenticated user has no identifiable claim.");
    }
}
```

- [ ] **Step 2: Run the new tests to confirm they pass**

Run from the repo root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BaseApiControllerTests"
```

Expected: 6/6 tests pass. (They pass immediately because the implementation already landed in Task 1 — these tests pin the behavior before further refactoring.)

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs
git commit -m "test(api): cover GetCurrentUserId() on BaseApiController"
```

---

## Task 3: Remove duplicate `GetCurrentUserId()` from `DashboardController`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs`

- [ ] **Step 1: Delete the private method**

Open `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs` and remove lines 97–105 (the entire `private string GetCurrentUserId()` block, including the surrounding blank line above it so the file ends cleanly at the closing brace of `DisableTile`). The file should now end:

```csharp
    [HttpPost("tiles/{tileId}/disable")]
    public async Task<ActionResult> DisableTile(string tileId)
    {
        var userId = GetCurrentUserId();
        var request = new DisableTileRequest
        {
            UserId = userId,
            TileId = tileId
        };
        await _mediator.Send(request);

        return Ok();
    }
}
```

- [ ] **Step 2: Remove the now-unused `using System.Security.Claims;`**

Edit `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs` line 1 — delete the `using System.Security.Claims;` line. The using block should now start at `using Anela.Heblo.Application.Features.Dashboard.UseCases.GetAvailableTiles;`.

- [ ] **Step 3: Delete the four obsolete tests from `DashboardControllerTests`**

Open `backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs` and remove lines 229–333 — the four `[Fact]` methods named `GetCurrentUserId_WhenNoClaimsPresent_ShouldThrowException`, `GetCurrentUserId_WhenSubClaimPresent_ShouldUseSubClaim`, `GetCurrentUserId_WhenOidClaimPresent_ShouldUseOidClaim`, and `GetCurrentUserId_WhenMultipleClaimsPresent_ShouldPrioritizeNameIdentifier`. The class should now end with the closing brace of the test method at line 227 (`DisableTile_WithValidTileId_ShouldReturnOk` — or whichever test ends at line 227) followed by the class closing brace.

The file's final lines should look like (the body above stays unchanged):

```csharp
        result.Should().BeOfType<OkResult>();

        _mediatorMock.Verify(x => x.Send(
            It.Is<DisableTileRequest>(r =>
                r.UserId == "test-user-123" &&
                r.TileId == tileId),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

Also remove `using System.Security.Claims;` from the file (line 14) **only if** no other surviving test references `ClaimTypes` or `Claim`. Verify with:

```bash
grep -n "Claim\|ClaimTypes" backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs
```

If the only remaining references are inside the constructor (lines 30–33) which still constructs `new Claim(ClaimTypes.NameIdentifier, …)`, **keep** the using directive. (The constructor sets up the `test-user-123` claim used by surviving tests.)

- [ ] **Step 4: Build and run `DashboardControllerTests`**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DashboardControllerTests"
```

Expected: build succeeds; all surviving `DashboardControllerTests` pass (the four `GetCurrentUserId_*` tests are gone; the remaining tests still verify the controller actions). `BaseApiControllerTests` from Task 2 now own the claim-fallback contract.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/DashboardController.cs \
        backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs
git commit -m "refactor(api): remove DashboardController.GetCurrentUserId() duplicate"
```

---

## Task 4: Remove duplicate `GetCurrentUserId()` from `CarrierCoolingController`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs`

- [ ] **Step 1: Delete the private method**

Open `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs` and remove lines 40–46 (the entire `private string GetCurrentUserId()` block, plus the blank line above it). The file should now end with:

```csharp
    [HttpPut]
    public async Task<ActionResult<SetCarrierCoolingResponse>> SetCooling(
        [FromBody] SetCarrierCoolingRequest request,
        CancellationToken cancellationToken = default)
    {
        request.ModifiedBy = GetCurrentUserId();
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
}
```

- [ ] **Step 2: Remove the now-unused `using System.Security.Claims;`**

Edit `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs` line 1 — delete the `using System.Security.Claims;` line. The using block should now start at `using Anela.Heblo.Application.Features.CarrierCooling.UseCases.GetCarrierCoolingMatrix;`.

- [ ] **Step 3: Verify no other reference remains**

```bash
grep -n "Claim\|ClaimTypes" backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs
```

Expected: no output. If anything is printed, restore the using.

- [ ] **Step 4: Build and run `CarrierCoolingControllerTests`**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CarrierCoolingControllerTests"
```

Expected: build succeeds; all tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs
git commit -m "refactor(api): remove CarrierCoolingController.GetCurrentUserId() duplicate"
```

---

## Task 5: Remove duplicate `GetCurrentUserId()` from `GiftSettingsController`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs`

- [ ] **Step 1: Delete the private method**

Open `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs` and remove lines 40–46 (the entire `private string GetCurrentUserId()` block, plus the blank line above it). The file should now end with:

```csharp
    [HttpPut]
    public async Task<IActionResult> SetGiftSetting(
        [FromBody] SetGiftSettingCommand command,
        CancellationToken cancellationToken = default)
    {
        command.ModifiedBy = GetCurrentUserId();
        var response = await _mediator.Send(command, cancellationToken);
        if (response.Success) return NoContent();
        return BadRequest(response);
    }
}
```

- [ ] **Step 2: Remove the now-unused `using System.Security.Claims;`**

Edit `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs` line 1 — delete the `using System.Security.Claims;` line. The using block should now start at `using Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting;`.

- [ ] **Step 3: Verify no other reference remains**

```bash
grep -n "Claim\|ClaimTypes" backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs
```

Expected: no output. If anything is printed, restore the using.

- [ ] **Step 4: Confirm the codebase-wide sweep finds no other definition**

This satisfies FR-2's mandatory pre-completion sweep. Run from the repo root:

```bash
grep -rn "private string GetCurrentUserId" backend/src --include="*.cs"
grep -rn "protected string GetCurrentUserId" backend/src --include="*.cs"
```

Expected output for the first command: **no matches**.
Expected output for the second command: **only** `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs`.

If any other controller still defines its own `GetCurrentUserId()`, repeat Tasks 3–5 for that controller before continuing.

- [ ] **Step 5: Build and run the full backend test suite**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build succeeds; the test suite that ran green before Task 1 is still green (minus the four deleted `DashboardControllerTests.GetCurrentUserId_*` tests, which have been re-homed to `BaseApiControllerTests`).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs
git commit -m "refactor(api): remove GiftSettingsController.GetCurrentUserId() duplicate"
```

---

## Task 6: Confirm no caller catches the (old) exception types

**Files:**
- (Read-only sweep — no edits expected.)

This satisfies NFR-3 plus Architecture Review Amendment #1 (sweep must include `InvalidOperationException`, not just `Exception`).

- [ ] **Step 1: Sweep for narrow catches of the previously-thrown types around call sites**

```bash
grep -rn "GetCurrentUserId" backend/src --include="*.cs"
```

For every line returned (all of which are now inside the three refactored controllers since the private methods are gone, leaving only call sites), open the surrounding action in the controller and confirm:

- The action does not wrap the call in `try { … } catch (Exception …)` or `try { … } catch (InvalidOperationException …)` that maps to a non-401 result. The three refactored controllers (`DashboardController`, `CarrierCoolingController`, `GiftSettingsController`) currently call `GetCurrentUserId()` directly with no surrounding try/catch — verified during Tasks 3–5.

- [ ] **Step 2: Sweep for any other thrower of `UnauthorizedAccessException`**

This satisfies Architecture Review Amendment #5.

```bash
grep -rn "throw new UnauthorizedAccessException\|throw new System.UnauthorizedAccessException" backend/src --include="*.cs"
```

Expected: only `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs` (from Task 1). If any other file is listed, open it and confirm that mapping it to 401 is acceptable. If not, the handler in Task 7 must scope itself further; flag the finding before proceeding.

- [ ] **Step 3: Sweep for any catcher of `UnauthorizedAccessException`**

```bash
grep -rn "catch (UnauthorizedAccessException\|catch(UnauthorizedAccessException" backend/src --include="*.cs"
```

Per the architecture review (Risk 2), the only existing reference is `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs:183`. That catch block consumes the exception locally and does **not** rely on a particular HTTP status, so the new 401 mapping does not affect it.

No commit for this task — it is verification only.

---

## Task 7: Create `UnauthorizedAccessExceptionHandler`

**Files:**
- Create: `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandler.cs`

- [ ] **Step 1: Create the directory and handler file**

Create `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandler.cs` with this exact content:

```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.API.Infrastructure.ExceptionHandling;

/// <summary>
/// Maps <see cref="UnauthorizedAccessException"/> to a 401 ProblemDetails response.
/// Body intentionally omits `detail`: the exception message is logged server-side only,
/// never returned to the client. This is the infrastructure-layer 401; business-layer
/// 401s flow through BaseApiController.HandleResponse and use the BaseResponse shape.
/// </summary>
public sealed class UnauthorizedAccessExceptionHandler : IExceptionHandler
{
    private readonly ILogger<UnauthorizedAccessExceptionHandler> _logger;

    public UnauthorizedAccessExceptionHandler(ILogger<UnauthorizedAccessExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not UnauthorizedAccessException uax)
        {
            return false;
        }

        _logger.LogWarning(uax, "Unauthorized access: {Message}", uax.Message);

        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
            Type = "https://tools.ietf.org/html/rfc7235#section-3.1"
        }, cancellationToken);

        return true;
    }
}
```

- [ ] **Step 2: Build the API project**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: build succeeds. (The handler is not yet registered or wired into the pipeline; that is Task 9.)

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandler.cs
git commit -m "feat(api): add UnauthorizedAccessExceptionHandler for 401 mapping"
```

---

## Task 8: Write unit tests for `UnauthorizedAccessExceptionHandler` (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandlerTests.cs`

- [ ] **Step 1: Write the test file**

Create `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandlerTests.cs` with this exact content:

```csharp
using System.Text.Json;
using Anela.Heblo.API.Infrastructure.ExceptionHandling;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure.ExceptionHandling;

public class UnauthorizedAccessExceptionHandlerTests
{
    private static (UnauthorizedAccessExceptionHandler Handler, DefaultHttpContext Context, MemoryStream Body) CreateSut()
    {
        var handler = new UnauthorizedAccessExceptionHandler(
            NullLogger<UnauthorizedAccessExceptionHandler>.Instance);
        var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        return (handler, context, body);
    }

    [Fact]
    public async Task TryHandleAsync_WhenUnauthorizedAccessException_Returns401WithProblemDetails()
    {
        var (handler, context, body) = CreateSut();
        var exception = new UnauthorizedAccessException("Authenticated user has no identifiable claim.");

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        body.Position = 0;
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(401);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Unauthorized");
        doc.RootElement.GetProperty("type").GetString().Should().Be("https://tools.ietf.org/html/rfc7235#section-3.1");
    }

    [Fact]
    public async Task TryHandleAsync_WhenUnauthorizedAccessException_DoesNotLeakMessageInBody()
    {
        var (handler, context, body) = CreateSut();
        var secretMessage = "INTERNAL-CLAIM-DEBUG-INFO-MUST-NOT-LEAK";
        var exception = new UnauthorizedAccessException(secretMessage);

        await handler.TryHandleAsync(context, exception, CancellationToken.None);

        body.Position = 0;
        var json = await new StreamReader(body).ReadToEndAsync();
        json.Should().NotContain(secretMessage);
        json.Should().NotContain("detail");
    }

    [Fact]
    public async Task TryHandleAsync_WhenOtherException_ReturnsFalseAndDoesNotWriteBody()
    {
        var (handler, context, body) = CreateSut();
        var exception = new InvalidOperationException("unrelated");

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeFalse();
        body.Length.Should().Be(0);
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK); // unchanged
    }
}
```

- [ ] **Step 2: Run the new tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UnauthorizedAccessExceptionHandlerTests"
```

Expected: 3/3 tests pass.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandlerTests.cs
git commit -m "test(api): cover UnauthorizedAccessExceptionHandler 401 mapping"
```

---

## Task 9: Register handler and wire into pipeline

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`
- Modify: `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs`

- [ ] **Step 1: Register the handler and ProblemDetails in DI**

Open `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`. Add a `using` at the top of the file (alphabetical placement keeps the diff clean — insert just below the existing `Anela.Heblo.API.Infrastructure.Telemetry` using at line 8):

```csharp
using Anela.Heblo.API.Infrastructure.ExceptionHandling;
```

Then, inside `AddCrossCuttingServices` (method at line 122), add the registrations immediately *after* the existing `services.AddSingleton(TimeProvider.System);` call (line 125). The method should look like:

```csharp
    public static IServiceCollection AddCrossCuttingServices(this IServiceCollection services)
    {
        // Register TimeProvider
        services.AddSingleton(TimeProvider.System);

        // Global exception → HTTP mapping for infrastructure exceptions.
        // Business errors continue to flow through BaseApiController.HandleResponse.
        services.AddExceptionHandler<UnauthorizedAccessExceptionHandler>();
        services.AddProblemDetails();

        // Register HttpClient for E2E testing middleware
        services.AddHttpClient();

        // …rest of the method unchanged…
```

- [ ] **Step 2: Wire `UseExceptionHandler()` into the pipeline**

Open `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs`. In `ConfigureApplicationPipeline`, insert the call *between* the E2E test auth block (ending at line 86) and `app.UseRouting()` (line 89). The relevant region becomes:

```csharp
        if (E2ETestAuthenticationMiddleware.ShouldBeRegistered(app))
        {
            app.UseMiddleware<E2ETestAuthenticationMiddleware>();
        }

        // Global exception handler — must come before UseRouting so it can catch
        // exceptions thrown by any later middleware (auth, endpoints, etc.).
        // Handler chain is composed via AddExceptionHandler<T>() in DI.
        app.UseExceptionHandler();

        // Routing must be explicitly configured before authentication/authorization
        app.UseRouting();
```

- [ ] **Step 3: Build**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Run the full test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: all tests pass. (No existing test relies on `UnauthorizedAccessException` reaching the client as a 500; the only catcher is `GraphService.cs:183`, which handles the exception locally — confirmed in Task 6 Step 3.)

- [ ] **Step 5: Format**

```bash
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: no changes, or only whitespace normalization. If the formatter rewrites a file, review the diff.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs \
        backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs
git commit -m "feat(api): wire UnauthorizedAccessExceptionHandler into pipeline"
```

---

## Task 10: Final validation

**Files:**
- (Verification only — no edits expected.)

- [ ] **Step 1: Backend build + format**

```bash
dotnet build
dotnet format --verify-no-changes
```

Expected: build succeeds; `dotnet format` exits 0 with no required changes.

- [ ] **Step 2: Full backend test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 3: Confirm only one `GetCurrentUserId` definition remains in `backend/src`**

```bash
grep -rn "string GetCurrentUserId" backend/src --include="*.cs"
```

Expected output: exactly one line, `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs:…  protected string GetCurrentUserId()`.

- [ ] **Step 4: Confirm no stray bare `throw new Exception("User not found")` remains**

```bash
grep -rn 'throw new Exception("User not found")' backend/src --include="*.cs"
```

Expected output: no matches.

- [ ] **Step 5: Confirm the spec's `UnauthorizedAccessException` is the only thrower in `backend/src`**

```bash
grep -rn "throw new UnauthorizedAccessException" backend/src --include="*.cs"
```

Expected output: one line, in `BaseApiController.cs`. (Re-verifies Task 6 Step 2 after refactors and registration.)

No commit for this task — it is verification only.

---

## Self-Review Notes (resolved before publishing)

- **Spec coverage:** FR-1 → Task 1. FR-2 → Tasks 3, 4, 5 (+ verification in Task 5 Step 4 and Task 10 Step 3). FR-3 → Tasks 7, 8, 9. FR-4 → Task 2 + Task 10. NFR-1 → no work needed (constant-time claim lookup). NFR-2 → preserved by Task 1's verbatim claim order; safe-body property pinned by Task 8 Step 1 second test. NFR-3 → Task 6. NFR-4 → Tasks 2 + 8.
- **Architecture Review amendments applied:** #1 (`InvalidOperationException` sweep) → Task 6 Step 1. #2 (using-directive removal) → Tasks 3 Step 2, 4 Step 2, 5 Step 2. #3 (test relocation) → Tasks 2 + 3 Step 3. #4 (`IExceptionHandler` mechanism + `ProblemDetails` body without `detail`) → Tasks 7, 8, 9. #5 (no-other-thrower check) → Task 6 Step 2. #6 (distinct body shape vs `HandleResponse`) → encoded in the handler's class-level comment in Task 7 Step 1.
- **Placeholders:** none — every code block is the full content to apply; every command states the expected output.
- **Type/method consistency:** the public surface is exactly one new `protected string GetCurrentUserId()` on `BaseApiController` and one new `public sealed class UnauthorizedAccessExceptionHandler : IExceptionHandler`. Names match between definition, registration (`AddExceptionHandler<UnauthorizedAccessExceptionHandler>()`), and test references.
