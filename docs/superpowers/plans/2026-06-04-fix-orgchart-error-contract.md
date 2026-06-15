# Fix OrgChartController Error Contract Violation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Align `OrgChartController` with the project-wide `BaseResponse` error envelope so HTTP 500 responses serialize as `OrgChartResponse` (not an anonymous `{error, message}` object) and stop leaking raw exception text / data-source URLs to clients.

**Architecture:** Move failure conversion from the controller to `GetOrganizationStructureHandler`: the handler catches `Exception` (re-throws `OperationCanceledException`), logs the exception server-side, and returns `new OrgChartResponse(ErrorCodes.InternalServerError)`. The controller becomes a thin pass-through that inherits `BaseApiController` and calls `HandleResponse(result)`, letting the existing `[HttpStatusCode]`-driven status-code mapping produce the 500 envelope. Exception messages from `OrgChartService` continue to exist but are never serialized — the handler intercepts them.

**Tech Stack:** .NET 8, ASP.NET Core MVC, MediatR, xUnit, FluentAssertions, Moq, `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>` via the existing `HebloWebApplicationFactory`).

---

## Files Touched

- **Modify** `backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs`
  - Change base class from `ControllerBase` to `BaseApiController`.
  - Remove `ILogger<OrgChartController>` field, constructor parameter, and `LogInformation` call.
  - Remove `try/catch`. Replace with `return HandleResponse(await _mediator.Send(...))`.
  - Update `[ProducesResponseType]` for 500 to declare `typeof(OrgChartResponse)`.

- **Modify** `backend/src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/GetOrganizationStructureHandler.cs`
  - Wrap the service call in a `try` block.
  - Re-throw `OperationCanceledException` (preserves cancellation semantics).
  - On any other `Exception`: log via `_logger.LogError(ex, "Failed to fetch organizational structure")` and return `new OrgChartResponse(ErrorCodes.InternalServerError)`.

- **Modify** `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs`
  - Replace `Handle_PropagatesException_WhenServiceThrows` with a test asserting the new contract (returns error envelope, logs once via `LogError`).
  - Add `Handle_RethrowsOperationCanceledException_WhenServiceCancels`.
  - Keep `Handle_ReturnsServiceResponse_WhenServiceSucceeds` as-is.

- **Create** `backend/test/Anela.Heblo.Tests/Controllers/OrgChartControllerTests.cs`
  - New integration test class using `HebloWebApplicationFactory`.
  - Replaces `IOrgChartService` with a test double via `WithWebHostBuilder` / `ConfigureTestServices` so the throwing path is reachable.
  - Asserts 500 status, `OrgChartResponse`-shaped body, and absence of leaked strings (URL, `"Failed to fetch organizational structure"`, `ex.Message`).
  - Asserts 200 status + populated body on the happy path.

No new files in `src/`. No new packages. No config changes.

---

## Task 1: Replace failing-path handler test (write the new RED test first)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs:48-75`

The existing test (`Handle_PropagatesException_WhenServiceThrows`) asserts the *old* "controller owns failure logging" convention. Under the new contract the handler **does not throw** and **does** call `LogError` exactly once. Replace it before changing the handler so we see a failing test first.

- [ ] **Step 1: Replace the failing-path test**

Replace the entire body of `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Anela.Heblo.Application.Features.OrgChart.Services;
using Anela.Heblo.Application.Features.OrgChart.UseCases.GetOrganizationStructure;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.OrgChart;

public class GetOrganizationStructureHandlerTests
{
    private readonly Mock<IOrgChartService> _orgChartServiceMock;
    private readonly Mock<ILogger<GetOrganizationStructureHandler>> _loggerMock;
    private readonly GetOrganizationStructureHandler _handler;

    public GetOrganizationStructureHandlerTests()
    {
        _orgChartServiceMock = new Mock<IOrgChartService>();
        _loggerMock = new Mock<ILogger<GetOrganizationStructureHandler>>();
        _handler = new GetOrganizationStructureHandler(_orgChartServiceMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsServiceResponse_WhenServiceSucceeds()
    {
        // Arrange
        var request = new GetOrganizationStructureRequest();
        var expected = new OrgChartResponse
        {
            Organization = new OrganizationDto { Name = "Anela" }
        };

        _orgChartServiceMock
            .Setup(x => x.GetOrganizationStructureAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
        _orgChartServiceMock.Verify(
            x => x.GetOrganizationStructureAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsErrorResponse_WhenServiceThrows()
    {
        // Arrange
        var request = new GetOrganizationStructureRequest();
        var thrown = new InvalidOperationException(
            "Failed to fetch organizational structure: https://sentinel.test/orgchart-source");

        _orgChartServiceMock
            .Setup(x => x.GetOrganizationStructureAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert: typed error envelope, no exception text leaked into Params.
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        result.Params.Should().BeNull();
        result.Organization.Should().NotBeNull();
        result.Organization.Positions.Should().BeEmpty();

        // Assert: handler logs exactly once with the original exception object (server-side diagnostics preserved).
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                thrown,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_RethrowsOperationCanceledException_WhenServiceCancels()
    {
        // Arrange
        var request = new GetOrganizationStructureRequest();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _orgChartServiceMock
            .Setup(x => x.GetOrganizationStructureAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        // Act
        var act = async () => await _handler.Handle(request, cts.Token);

        // Assert: cancellation propagates so callers see the request was aborted, not failed.
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Assert: cancellation is not logged as an error.
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run the handler tests and verify the new tests FAIL**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetOrganizationStructureHandlerTests" \
  --nologo --verbosity minimal
```

Expected:
- `Handle_ReturnsServiceResponse_WhenServiceSucceeds` — **PASS** (unchanged behavior).
- `Handle_ReturnsErrorResponse_WhenServiceThrows` — **FAIL**. The current handler still throws on service exceptions, so the test will report an unhandled `InvalidOperationException` instead of the expected error response.
- `Handle_RethrowsOperationCanceledException_WhenServiceCancels` — **PASS** by accident (the current handler also propagates exceptions), but the `Times.Never` log assertion still holds. Acceptable.

Do **not** commit yet — the RED step in TDD is only complete once we see the failing test for the behavior we're about to add.

---

## Task 2: Update handler to catch, log, and return typed error envelope (GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/GetOrganizationStructureHandler.cs`

- [ ] **Step 1: Add `using` for `ErrorCodes`**

At the top of the file, alongside the existing usings, add:

```csharp
using Anela.Heblo.Application.Shared;
```

- [ ] **Step 2: Replace the `Handle` method body**

Replace the existing `Handle` method (currently lines 24-28) with:

```csharp
    public async Task<OrgChartResponse> Handle(GetOrganizationStructureRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling request to fetch organizational structure");
        try
        {
            return await _orgChartService.GetOrganizationStructureAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch organizational structure");
            return new OrgChartResponse(ErrorCodes.InternalServerError);
        }
    }
```

The final file should be:

```csharp
using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Anela.Heblo.Application.Features.OrgChart.Services;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.OrgChart.UseCases.GetOrganizationStructure;

/// <summary>
/// Handler for retrieving the complete organizational structure
/// </summary>
public class GetOrganizationStructureHandler : IRequestHandler<GetOrganizationStructureRequest, OrgChartResponse>
{
    private readonly IOrgChartService _orgChartService;
    private readonly ILogger<GetOrganizationStructureHandler> _logger;

    public GetOrganizationStructureHandler(
        IOrgChartService orgChartService,
        ILogger<GetOrganizationStructureHandler> logger)
    {
        _orgChartService = orgChartService;
        _logger = logger;
    }

    public async Task<OrgChartResponse> Handle(GetOrganizationStructureRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling request to fetch organizational structure");
        try
        {
            return await _orgChartService.GetOrganizationStructureAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch organizational structure");
            return new OrgChartResponse(ErrorCodes.InternalServerError);
        }
    }
}
```

- [ ] **Step 3: Run the handler tests and verify ALL three PASS**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetOrganizationStructureHandlerTests" \
  --nologo --verbosity minimal
```

Expected: 3 tests passed.

- [ ] **Step 4: Commit the handler change**

```bash
git add backend/src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/GetOrganizationStructureHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs
git commit -m "fix(orgchart): convert service exceptions to typed error response in handler"
```

---

## Task 3: Convert controller to BaseApiController + HandleResponse (GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs`

- [ ] **Step 1: Rewrite the controller**

Overwrite `backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs` with:

```csharp
using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Anela.Heblo.Application.Features.OrgChart.UseCases.GetOrganizationStructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>
/// Controller for organizational chart operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrgChartController : BaseApiController
{
    private readonly IMediator _mediator;

    public OrgChartController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Gets the complete organizational structure
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Organization chart with all positions and employees</returns>
    /// <response code="200">Returns the organizational structure</response>
    /// <response code="500">If there was an error fetching the data</response>
    [HttpGet]
    [ProducesResponseType(typeof(OrgChartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OrgChartResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OrgChartResponse>> GetOrganizationStructure(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOrganizationStructureRequest(), cancellationToken);
        return HandleResponse(result);
    }
}
```

Key changes from the previous version:
- Base class is `BaseApiController` (not `ControllerBase`).
- No `ILogger<OrgChartController>` field or constructor parameter (the handler logs).
- No `try/catch` block, no anonymous `{ error, message }` object.
- 500 `[ProducesResponseType]` now declares `typeof(OrgChartResponse)` for OpenAPI accuracy.

- [ ] **Step 2: Build the backend to catch compile errors**

Run:
```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --nologo --verbosity minimal
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Run the full backend test suite to confirm nothing regressed**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo --verbosity minimal
```

Expected: all tests pass. Pay attention to any startup/composition-root tests that scan controllers — `BaseApiController` is already used by 43 of 57 controllers, so no DI surprise is expected.

- [ ] **Step 4: Commit the controller refactor**

```bash
git add backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs
git commit -m "fix(orgchart): inherit BaseApiController and use HandleResponse for error mapping"
```

---

## Task 4: Add controller integration test for happy path + 500 leak-prevention

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Controllers/OrgChartControllerTests.cs`

This test pins the new wire-level contract: the 500 body is typed `OrgChartResponse`, contains no leaked URL or exception text, and the 200 happy path still returns a populated structure. It uses the existing `HebloWebApplicationFactory` with a per-test `WithWebHostBuilder` override that swaps `IOrgChartService` for a controllable test double.

- [ ] **Step 1: Write the new integration test file**

Create `backend/test/Anela.Heblo.Tests/Controllers/OrgChartControllerTests.cs` with:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Anela.Heblo.Application.Features.OrgChart.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class OrgChartControllerTests : IClassFixture<HebloWebApplicationFactory>
{
    private const string SentinelDataSourceUrl = "https://sentinel.test/orgchart-source";
    private const string SentinelWrappedMessage =
        "Failed to fetch organizational structure: " + SentinelDataSourceUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HebloWebApplicationFactory _factory;

    public OrgChartControllerTests(HebloWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Returns200WithPopulatedBody_WhenServiceSucceeds()
    {
        // Arrange
        var expected = new OrgChartResponse
        {
            Organization = new OrganizationDto
            {
                Name = "Anela",
                Positions =
                {
                    new PositionDto { Title = "CEO" }
                }
            }
        };
        var client = CreateClientWithService(new StubOrgChartService(expected));

        // Act
        var response = await client.GetAsync("/api/OrgChart");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OrgChartResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.ErrorCode.Should().BeNull();
        body.Organization.Name.Should().Be("Anela");
        body.Organization.Positions.Should().HaveCount(1);
        body.Organization.Positions[0].Title.Should().Be("CEO");
    }

    [Fact]
    public async Task Get_Returns500WithTypedErrorEnvelope_WhenServiceThrows()
    {
        // Arrange: the stub throws the SAME shape of exception that OrgChartService produces
        // (wraps the data-source URL and underlying message in InvalidOperationException).
        var thrown = new InvalidOperationException(SentinelWrappedMessage);
        var client = CreateClientWithService(new ThrowingOrgChartService(thrown));

        // Act
        var response = await client.GetAsync("/api/OrgChart");

        // Assert: status code mapping via [HttpStatusCode] on ErrorCodes.InternalServerError.
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // Assert: body deserializes as OrgChartResponse with the typed error envelope.
        var body = await response.Content.ReadFromJsonAsync<OrgChartResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        body.Params.Should().BeNull();
        body.Organization.Should().NotBeNull();
        body.Organization.Positions.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_500BodyDoesNotLeakDataSourceUrlOrWrappedExceptionMessage()
    {
        // Arrange
        var thrown = new InvalidOperationException(SentinelWrappedMessage);
        var client = CreateClientWithService(new ThrowingOrgChartService(thrown));

        // Act
        var response = await client.GetAsync("/api/OrgChart");
        var rawBody = await response.Content.ReadAsStringAsync();

        // Assert: no leakage of configuration values or wrapped exception text.
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        rawBody.Should().NotContain(SentinelDataSourceUrl);
        rawBody.Should().NotContain("Failed to fetch organizational structure");
        rawBody.Should().NotContain("InvalidOperationException");
    }

    private HttpClient CreateClientWithService(IOrgChartService service)
    {
        return _factory
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    var existing = services
                        .Where(s => s.ServiceType == typeof(IOrgChartService))
                        .ToList();
                    foreach (var descriptor in existing)
                    {
                        services.Remove(descriptor);
                    }
                    services.AddSingleton(service);
                });
            })
            .CreateClient();
    }

    private sealed class StubOrgChartService : IOrgChartService
    {
        private readonly OrgChartResponse _response;

        public StubOrgChartService(OrgChartResponse response) => _response = response;

        public Task<OrgChartResponse> GetOrganizationStructureAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_response);
    }

    private sealed class ThrowingOrgChartService : IOrgChartService
    {
        private readonly Exception _exception;

        public ThrowingOrgChartService(Exception exception) => _exception = exception;

        public Task<OrgChartResponse> GetOrganizationStructureAsync(CancellationToken cancellationToken = default)
            => throw _exception;
    }
}
```

Notes for implementers:
- `IOrgChartService` is the seam: `OrgChartService` itself is constructed with `HttpClient` + `IOptions<OrgChartOptions>` via DI in `OrgChartModule.cs`. Swapping the interface registration in `ConfigureTestServices` is sufficient — no need to mock `HttpMessageHandler`.
- The integration test class does **not** implement `IAsyncLifetime` because it does not touch the database.
- `Microsoft.AspNetCore.Mvc.Testing` is already referenced by the test project (other controller tests in the same folder use it). No new package.
- `using Microsoft.AspNetCore.Mvc.Testing;` is required for `WithWebHostBuilder`.

- [ ] **Step 2: Run the new integration tests and verify all three PASS**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~OrgChartControllerTests" \
  --nologo --verbosity minimal
```

Expected: 3 tests passed.

If `Get_Returns500WithTypedErrorEnvelope_WhenServiceThrows` reports the status as `200` or returns an empty body, double-check that:
- `OrgChartController` extends `BaseApiController` (Task 3) and calls `HandleResponse`.
- `GetOrganizationStructureHandler` returns `new OrgChartResponse(ErrorCodes.InternalServerError)` rather than throwing (Task 2).

- [ ] **Step 3: Run the full backend test suite to confirm no regressions**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo --verbosity minimal
```

Expected: all tests pass, including pre-existing tests under `Controllers/`, `Features/`, and startup/composition-root scans.

- [ ] **Step 4: Run `dotnet format` to normalize formatting**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verbosity minimal
```

Expected: completes without warnings on the modified files; any whitespace changes apply only to the files this plan touched.

- [ ] **Step 5: Commit the integration tests**

```bash
git add backend/test/Anela.Heblo.Tests/Controllers/OrgChartControllerTests.cs
git commit -m "test(orgchart): integration tests for typed 500 envelope and leak prevention"
```

---

## Task 5: Validate OpenAPI / TypeScript client regeneration

The TypeScript client is auto-generated on backend build per `docs/development/api-client-generation.md`. The 500 `[ProducesResponseType]` schema changed from untyped to `OrgChartResponse`, so regenerated client code may add/modify a type. Confirm the change is benign before committing.

- [ ] **Step 1: Build the backend (triggers OpenAPI generation)**

Run:
```bash
dotnet build backend/Anela.Heblo.sln --nologo --verbosity minimal
```

Expected: build succeeds. If the build wires OpenAPI export, the generated `swagger.json` / OpenAPI document under `backend/` (or wherever the project emits it — see `docs/development/api-client-generation.md`) should be updated automatically.

- [ ] **Step 2: Build the frontend to verify the regenerated TS client still compiles**

Run:
```bash
cd frontend && npm run build
```

Expected: `npm run build` completes without TypeScript errors. The OrgChart endpoint should now type both 200 and 500 responses as `OrgChartResponse`.

- [ ] **Step 3: Inspect the diff for any generated artifacts**

Run:
```bash
git status
git diff --stat
```

Inspect any generated files (look for paths like `backend/.../swagger.json`, `frontend/src/api/`, or similar — see `docs/development/api-client-generation.md` for the canonical paths). The expected diff:
- `[ProducesResponseType]` change for 500 may add `OrgChartResponse` to the generated 500 response schema.
- No other endpoint contracts should change.

If the only generated changes are scoped to the OrgChart endpoint, stage and commit them:

```bash
# Stage only the regenerated artifacts that show up under `git status`.
# Use `git add -p` if multiple files are affected and inspection is needed.
git add <generated artifacts>
git commit -m "chore(orgchart): regenerate OpenAPI artifacts after error-contract fix"
```

If the regenerated files contain unrelated diffs (e.g. ordering changes spanning many endpoints), do not lump them into this commit — pause, investigate, and decide whether to include them in this PR or split them. The architecture review explicitly flagged this as a low-risk inspection point, not a blocker.

- [ ] **Step 4: Frontend lint check**

Run:
```bash
cd frontend && npm run lint
```

Expected: no new lint errors introduced by regenerated client. If errors only exist in pre-existing files, they are out of scope for this change.

---

## Task 6: Final verification before completion

- [ ] **Step 1: Run the full backend build + test once more**

Run:
```bash
dotnet build backend/Anela.Heblo.sln --nologo --verbosity minimal && \
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo --verbosity minimal
```

Expected: build succeeds, all tests pass.

- [ ] **Step 2: Confirm `git status` shows only the intended files**

Run:
```bash
git status
```

Expected modified/created paths:
- `backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs` (modified)
- `backend/src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/GetOrganizationStructureHandler.cs` (modified)
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs` (modified)
- `backend/test/Anela.Heblo.Tests/Controllers/OrgChartControllerTests.cs` (created)
- Optionally: regenerated OpenAPI / TS client artifacts from Task 5.

If unrelated files appear in `git status`, investigate before pushing — surgical changes only.

- [ ] **Step 3: Confirm no `_options.DataSourceUrl` or wrapped exception text appears in the response by manual scan**

Run:
```bash
git diff main -- backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs
```

Verify the new controller body contains:
- `: BaseApiController`
- `HandleResponse(result)`

And does **not** contain:
- `new {`
- `ex.Message`
- `try` / `catch`

---

## Self-Review

**Spec coverage check:**

| Spec section | Covered by |
|---|---|
| FR-1: 500 conforms to `OrgChartResponse` | Tasks 2, 3, 4 (handler returns typed envelope; controller declares it via `[ProducesResponseType]`; integration test asserts shape). |
| FR-2: Stop forwarding raw exception text | Tasks 2, 4 (handler swallows + logs; integration test asserts URL + wrapped message absence in body). |
| FR-3: Align with project-wide envelope (option A) | Tasks 2, 3 (handler returns `OrgChartResponse(ErrorCodes.InternalServerError)`; controller uses `BaseApiController.HandleResponse`). New replacement test in Task 1. |
| FR-4: 200 success path unchanged | Tasks 1 (handler success test retained), 4 (integration test 200 happy path). |
| NFR-1 Performance | No happy-path change — verified by Task 2 (no work added on success branch). |
| NFR-2 Security: server-side log preserved | Task 1 asserts `LogError` is called once with the original exception. |
| NFR-3 Consistency | Task 3 — controller inherits `BaseApiController`, identical pattern to the other 42 controllers. |
| NFR-4 OpenAPI / TS client compatibility | Task 5 — backend + frontend builds verify TS client regenerates cleanly. |
| NFR-5 Test coverage | Tasks 1, 4 — new failing-path handler test, new failing-path integration test. |
| Arch amendment 1: replace `Handle_PropagatesException_WhenServiceThrows` | Task 1 replaces the test wholesale, including the `LogError` Times.Once assertion. |
| Arch amendment 2: drop `_logger` from controller | Task 3 — removed `ILogger` field, constructor parameter, and `LogInformation` call. |
| Arch amendment 3: integration test placement | Task 4 — uses `HebloWebApplicationFactory` (`backend/test/Anela.Heblo.Tests/Common/HebloWebApplicationFactory.cs`) which already supports `WithWebHostBuilder` overrides; tests live under `backend/test/Anela.Heblo.Tests/Controllers/` next to existing controller tests like `CarrierCoolingControllerTests.cs`. |
| `OperationCanceledException` rethrow (Arch decision 2) | Tasks 1 (`Handle_RethrowsOperationCanceledException_WhenServiceCancels`), 2 (`catch (OperationCanceledException) { throw; }`). |

**Placeholder scan:** None. Every step contains the full code, command, or assertion needed.

**Type consistency:** `OrgChartResponse`, `ErrorCodes.InternalServerError`, `IOrgChartService.GetOrganizationStructureAsync`, `BaseApiController.HandleResponse`, `OrganizationDto`, `PositionDto` — all match the on-disk definitions verified before writing the plan.
