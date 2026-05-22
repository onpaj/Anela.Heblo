# Photobank Thumbnail Use Case Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the `PhotobankController.GetThumbnail` orchestration into a `GetThumbnail` MediatR use case, reduce the controller to a thin dispatch with an `IMediator`-only constructor, and preserve the endpoint's exact observable HTTP behaviour.

**Architecture:** A new Vertical-Slice use case (`Features/Photobank/UseCases/GetThumbnail/{Request,Response,Handler}.cs`) owns the locator lookup, the Microsoft Graph call, and the infrastructure-exception translation. The handler returns a `GetThumbnailResponse : BaseResponse` carrying the stream + metadata on success and a discriminating `ErrorCodes` on failure (mirroring the three existing binary-download use cases — `GetShipmentLabelPdf`, `DownloadExpeditionList`, `GetManufactureProtocol`). The controller maps `ErrorCode → HTTP status + headers` explicitly (it does **not** route through `HandleResponse`, which has no 502/503 and wraps success in `Ok()`).

**Tech Stack:** .NET 8, MediatR, ASP.NET Core MVC, xUnit + FluentAssertions + Moq. No new packages (MSAL is already a transitive reference of `Anela.Heblo.Application`, used by `UserManagement/Services/GraphService.cs` and others).

---

## Authoritative decision: response shape

The architecture review **reverses spec Decision #2**. Follow the arch-review:

- `GetThumbnailResponse` **inherits `BaseResponse`** (it does NOT use a bespoke `GetThumbnailOutcome` enum). Inheriting `BaseResponse` does not force a JSON success body — the controller still returns `FileStreamResult` on success, exactly as `ShipmentLabelsController.GetLabelPdf` returns `File(...)`.
- Failures are discriminated by **four new `ErrorCodes` members**, not an enum.
- The spec's *intent* — preserving the 502-vs-503-vs-404 distinction — is kept.

## File map

| File | Action | Responsibility |
|------|--------|----------------|
| `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` | Modify | Add 4 Photobank-thumbnail error codes (2610–2613) |
| `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailRequest.cs` | Create | `IRequest<GetThumbnailResponse>` with `Id`, `Size` |
| `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailResponse.cs` | Create | `BaseResponse` carrier: `Content`, `ContentType`, `ContentLength`, `RetryAfterSeconds` |
| `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailHandler.cs` | Create | Locator lookup → Graph call → exception translation → result shaping + all logging |
| `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` | Modify | Single-dep constructor; thin `GetThumbnail` dispatch + outcome→HTTP mapping |
| `backend/test/Anela.Heblo.Tests/Features/Photobank/GetThumbnailHandlerTests.cs` | Create | HTTP-free handler unit tests (all outcomes + stream identity + token threading) |
| `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankControllerThumbnailTests.cs` | Overwrite | Mock `IMediator` only; assert outcome→`IActionResult` + header mapping |

**Verified facts the executor can rely on:**
- `IPhotobankRepository.GetLocatorAsync(int, CancellationToken) → PhotoLocator?` exists (`Domain/Features/Photobank/IPhotobankRepository.cs:26`). `PhotoLocator(string DriveId, string SharePointFileId, DateTime ModifiedAt)`.
- `IPhotobankGraphService.GetThumbnailAsync(string driveId, string fileId, ThumbnailSize size, CancellationToken) → GraphThumbnail?` exists (`Application/Features/Photobank/Services/IPhotobankGraphService.cs:54`).
- `GraphThumbnail : IDisposable` with `Stream Content`, `string ContentType`, `long? ContentLength`; `Dispose()` disposes the `Stream`. `ThumbnailSize { Medium, Large }`. `GraphThrottledException.RetryAfter` is `TimeSpan?`.
- `BaseResponse` (`Application/Shared`) has `bool Success`, `ErrorCodes? ErrorCode`, a default ctor (`Success = true`) and `protected BaseResponse(ErrorCodes errorCode, ...)`.
- Photobank use-case files use **block-scoped** namespaces (`namespace X { ... }`) — match that style, not file-scoped.
- The Application project has `<ImplicitUsings>` enabled (the sibling `GetShipmentLabelPdfResponse` uses `Stream?` with no `using System.IO;`).
- MediatR auto-registers handlers via the existing assembly scan — no `PhotobankModule.cs` / DI change needed.

---

### Task 1: Add the four thumbnail error codes

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`

- [ ] **Step 1: Add the error codes**

In `ErrorCodes.cs`, find the Photobank block ending at `PhotobankInvalidRegexPattern = 2609,` (around line 285). Immediately after that line and before the `// Smartsupp module errors (27XX)` comment, insert:

```csharp
    [HttpStatusCode(HttpStatusCode.NotFound)]
    PhotobankThumbnailNotFound = 2610,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    PhotobankThumbnailThrottled = 2611,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    PhotobankThumbnailAuthUnavailable = 2612,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    PhotobankThumbnailUpstream = 2613,
```

> Note: the `[HttpStatusCode(...)]` attributes are decorative for convention only — this endpoint maps `ErrorCode → status` explicitly in the controller and never calls `HandleResponse`. There is no 502 in the `HttpStatusCode` attribute set used here, so `PhotobankThumbnailUpstream` carries `InternalServerError`; the controller maps it to 502 directly.

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat: add Photobank thumbnail error codes"
```

---

### Task 2: Create the request and response contracts

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailResponse.cs`

- [ ] **Step 1: Create `GetThumbnailResponse.cs`**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetThumbnail
{
    public class GetThumbnailResponse : BaseResponse
    {
        public Stream? Content { get; set; }
        public string? ContentType { get; set; }
        public long? ContentLength { get; set; }

        /// <summary>
        /// Pre-rounded retry hint (seconds). Populated only on PhotobankThumbnailThrottled.
        /// </summary>
        public int? RetryAfterSeconds { get; set; }

        public GetThumbnailResponse() : base()
        {
        }

        public GetThumbnailResponse(ErrorCodes errorCode) : base(errorCode)
        {
        }
    }
}
```

- [ ] **Step 2: Create `GetThumbnailRequest.cs`**

```csharp
using Anela.Heblo.Application.Features.Photobank.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetThumbnail
{
    public class GetThumbnailRequest : IRequest<GetThumbnailResponse>
    {
        public int Id { get; set; }
        public ThumbnailSize Size { get; set; }
    }
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/
git commit -m "feat: add GetThumbnail request and response contracts"
```

---

### Task 3: Implement the handler (TDD)

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Photobank/GetThumbnailHandlerTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailHandler.cs`

- [ ] **Step 1: Write the failing handler tests**

Create `backend/test/Anela.Heblo.Tests/Features/Photobank/GetThumbnailHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetThumbnail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Moq;
using System.Net.Http;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public sealed class GetThumbnailHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repositoryMock = new();
    private readonly Mock<IPhotobankGraphService> _graphServiceMock = new();
    private readonly Mock<ILogger<GetThumbnailHandler>> _loggerMock = new();

    private GetThumbnailHandler CreateHandler() =>
        new(_repositoryMock.Object, _graphServiceMock.Object, _loggerMock.Object);

    private static GetThumbnailRequest Request(int id = 1, ThumbnailSize size = ThumbnailSize.Medium) =>
        new() { Id = id, Size = size };

    private void SetupLocator(PhotoLocator? locator, int id = 1) =>
        _repositoryMock
            .Setup(r => r.GetLocatorAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(locator);

    private void SetupGraph(PhotoLocator locator, Func<Mock<IPhotobankGraphService>, Moq.Language.Flow.ISetup<IPhotobankGraphService, Task<GraphThumbnail?>>> _ = null!)
    {
        // helper intentionally minimal; tests configure the graph mock directly
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenLocatorMissing()
    {
        // Arrange
        SetupLocator(null);

        // Act
        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PhotobankThumbnailNotFound);
        _graphServiceMock.Verify(
            g => g.GetThumbnailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ThumbnailSize>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenGraphReturnsNull()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        SetupLocator(locator);
        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GraphThumbnail?)null);

        // Act
        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PhotobankThumbnailNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsThrottledWithRoundedRetryAfter_WhenGraphThrottles()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        SetupLocator(locator);
        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GraphThrottledException(TimeSpan.FromSeconds(29.3)));

        // Act
        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PhotobankThumbnailThrottled);
        response.RetryAfterSeconds.Should().Be(30);
    }

    [Fact]
    public async Task Handle_ReturnsThrottledWithoutRetryAfter_WhenRetryAfterNull()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        SetupLocator(locator);
        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GraphThrottledException(null));

        // Act
        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        // Assert
        response.ErrorCode.Should().Be(ErrorCodes.PhotobankThumbnailThrottled);
        response.RetryAfterSeconds.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsUpstream_WhenHttpRequestExceptionThrown()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        SetupLocator(locator);
        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("upstream error"));

        // Act
        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PhotobankThumbnailUpstream);
    }

    [Fact]
    public async Task Handle_ReturnsAuthUnavailable_WhenMsalExceptionThrown()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        SetupLocator(locator);
        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MsalServiceException("invalid_client", "AADSTS7000215: Invalid client secret"));

        // Act
        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PhotobankThumbnailAuthUnavailable);
    }

    [Fact]
    public async Task Handle_ReturnsSuccessWithSameStream_WhenThumbnailReturned()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        var thumbnail = new GraphThumbnail(stream, "image/jpeg", 12345L);
        SetupLocator(locator);
        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ReturnsAsync(thumbnail);

        // Act
        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Content.Should().BeSameAs(stream);
        response.ContentType.Should().Be("image/jpeg");
        response.ContentLength.Should().Be(12345L);
        response.Content!.CanRead.Should().BeTrue("the handler must not dispose the stream before the framework writes it");
    }

    [Fact]
    public async Task Handle_PassesCancellationTokenThrough()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        using var cts = new CancellationTokenSource();
        _repositoryMock
            .Setup(r => r.GetLocatorAsync(1, cts.Token))
            .ReturnsAsync(locator);
        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, cts.Token))
            .ReturnsAsync((GraphThumbnail?)null);

        // Act
        await CreateHandler().Handle(Request(), cts.Token);

        // Assert
        _repositoryMock.Verify(r => r.GetLocatorAsync(1, cts.Token), Times.Once);
        _graphServiceMock.Verify(
            g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, cts.Token),
            Times.Once);
    }
}
```

> Remove the unused `SetupGraph` helper before finishing if `dotnet format`/analyzers complain — it is included only as a no-op placeholder and the tests configure `_graphServiceMock` directly. Simpler: delete the `SetupGraph` method entirely; it is not referenced by any test. **Delete it now.**

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetThumbnailHandlerTests"`
Expected: FAIL — compile error `The type or namespace name 'GetThumbnailHandler' could not be found` (the handler does not exist yet).

- [ ] **Step 3: Implement the handler**

Create `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailHandler.cs`:

```csharp
using System.Net.Http;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetThumbnail
{
    public class GetThumbnailHandler : IRequestHandler<GetThumbnailRequest, GetThumbnailResponse>
    {
        private readonly IPhotobankRepository _repository;
        private readonly IPhotobankGraphService _graphService;
        private readonly ILogger<GetThumbnailHandler> _logger;

        public GetThumbnailHandler(
            IPhotobankRepository repository,
            IPhotobankGraphService graphService,
            ILogger<GetThumbnailHandler> logger)
        {
            _repository = repository;
            _graphService = graphService;
            _logger = logger;
        }

        public async Task<GetThumbnailResponse> Handle(
            GetThumbnailRequest request,
            CancellationToken cancellationToken)
        {
            var locator = await _repository.GetLocatorAsync(request.Id, cancellationToken);
            if (locator is null)
            {
                return new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailNotFound);
            }

            GraphThumbnail? rawThumbnail;
            try
            {
                rawThumbnail = await _graphService.GetThumbnailAsync(
                    locator.DriveId, locator.SharePointFileId, request.Size, cancellationToken);
            }
            catch (GraphThrottledException ex)
            {
                _logger.LogWarning("Microsoft Graph thumbnail request throttled for photo {PhotoId}. RetryAfter: {RetryAfter}",
                    request.Id, ex.RetryAfter);
                return new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailThrottled)
                {
                    RetryAfterSeconds = ex.RetryAfter.HasValue
                        ? (int)Math.Ceiling(ex.RetryAfter.Value.TotalSeconds)
                        : null,
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Upstream HTTP error fetching thumbnail for photo {PhotoId}", request.Id);
                return new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailUpstream);
            }
            catch (MsalException ex)
            {
                _logger.LogError(ex, "Token acquisition failed for thumbnail {PhotoId}. MSAL error: {ErrorCode}", request.Id, ex.ErrorCode);
                return new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailAuthUnavailable);
            }

            if (rawThumbnail is null)
            {
                return new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailNotFound);
            }

            // NFR-3: transfer stream ownership to the response. Do NOT dispose rawThumbnail
            // (GraphThumbnail.Dispose() closes the underlying Stream); FileStreamResult disposes it after writing.
            return new GetThumbnailResponse
            {
                Content = rawThumbnail.Content,
                ContentType = rawThumbnail.ContentType,
                ContentLength = rawThumbnail.ContentLength,
            };
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetThumbnailHandlerTests"`
Expected: PASS — 8 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/GetThumbnailHandlerTests.cs
git commit -m "feat: add GetThumbnail handler with HTTP-free unit tests"
```

---

### Task 4: Refactor the controller to a thin dispatch (TDD)

**Files:**
- Overwrite: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankControllerThumbnailTests.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs`

- [ ] **Step 1: Rewrite the controller test to mock `IMediator` only**

Overwrite `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankControllerThumbnailTests.cs` with:

```csharp
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetThumbnail;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public sealed class PhotobankControllerThumbnailTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly PhotobankController _controller;

    public PhotobankControllerThumbnailTests()
    {
        _controller = new PhotobankController(_mediatorMock.Object);
        SetupHttpContext();
    }

    private void SetupHttpContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "test-user") })),
            RequestServices = services.BuildServiceProvider()
        };

        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private void SetupResponse(GetThumbnailResponse response) =>
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetThumbnailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

    [Fact]
    public async Task GetThumbnail_ReturnsNotFound_WhenResponseNotFound()
    {
        // Arrange
        SetupResponse(new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailNotFound));

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetThumbnail_Returns503WithRetryAfter_WhenThrottledWithHint()
    {
        // Arrange
        SetupResponse(new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailThrottled) { RetryAfterSeconds = 30 });

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        var statusResult = result.Should().BeOfType<StatusCodeResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _controller.Response.Headers["Retry-After"].ToString().Should().Be("30");
    }

    [Fact]
    public async Task GetThumbnail_Returns503WithoutRetryAfter_WhenThrottledWithoutHint()
    {
        // Arrange
        SetupResponse(new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailThrottled) { RetryAfterSeconds = null });

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        var statusResult = result.Should().BeOfType<StatusCodeResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _controller.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task GetThumbnail_Returns503_WhenAuthUnavailable()
    {
        // Arrange
        SetupResponse(new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailAuthUnavailable));

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        var statusResult = result.Should().BeOfType<StatusCodeResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _controller.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task GetThumbnail_Returns502_WhenUpstreamError()
    {
        // Arrange
        SetupResponse(new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailUpstream));

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        var statusResult = result.Should().BeOfType<StatusCodeResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public async Task GetThumbnail_Returns200WithCacheHeaders_WhenSuccessful()
    {
        // Arrange
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        SetupResponse(new GetThumbnailResponse { Content = stream, ContentType = "image/jpeg", ContentLength = null });

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        var fileResult = result.Should().BeOfType<FileStreamResult>().Subject;
        fileResult.ContentType.Should().Be("image/jpeg");
        fileResult.FileStream.Should().BeSameAs(stream);
        _controller.Response.Headers["Cache-Control"].ToString().Should().Be("public, max-age=31536000, immutable");
    }

    [Fact]
    public async Task GetThumbnail_ForwardsContentLength_WhenAvailable()
    {
        // Arrange
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        SetupResponse(new GetThumbnailResponse { Content = stream, ContentType = "image/jpeg", ContentLength = 12345L });

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        result.Should().BeOfType<FileStreamResult>();
        _controller.Response.ContentLength.Should().Be(12345L);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankControllerThumbnailTests"`
Expected: FAIL — compile error: `PhotobankController` does not contain a constructor that takes 1 argument (it still takes 3). This is the RED state.

- [ ] **Step 3: Replace the controller constructor and fields**

In `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs`, replace this block:

```csharp
        private readonly IMediator _mediator;
        private readonly IPhotobankRepository _photobankRepository;
        private readonly IPhotobankGraphService _photobankGraphService;

        public PhotobankController(
            IMediator mediator,
            IPhotobankRepository photobankRepository,
            IPhotobankGraphService photobankGraphService)
        {
            _mediator = mediator;
            _photobankRepository = photobankRepository;
            _photobankGraphService = photobankGraphService;
        }
```

with:

```csharp
        private readonly IMediator _mediator;

        public PhotobankController(IMediator mediator)
        {
            _mediator = mediator;
        }
```

- [ ] **Step 4: Replace the `GetThumbnail` action**

Replace the entire current `GetThumbnail` action (the method beginning at `public async Task<IActionResult> GetThumbnail(` through its closing brace) with:

```csharp
        [HttpGet("photos/{id:int}/thumbnail/{size}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> GetThumbnail(
            int id,
            ThumbnailSize size,
            CancellationToken cancellationToken = default)
        {
            var response = await _mediator.Send(
                new GetThumbnailRequest { Id = id, Size = size }, cancellationToken);

            if (response.Success)
            {
                Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
                if (response.ContentLength.HasValue)
                {
                    Response.ContentLength = response.ContentLength;
                }

                return new FileStreamResult(response.Content!, response.ContentType!);
            }

            switch (response.ErrorCode)
            {
                case ErrorCodes.PhotobankThumbnailNotFound:
                    return NotFound();
                case ErrorCodes.PhotobankThumbnailThrottled:
                    if (response.RetryAfterSeconds.HasValue)
                    {
                        Response.Headers["Retry-After"] = response.RetryAfterSeconds.Value.ToString();
                    }
                    return StatusCode(StatusCodes.Status503ServiceUnavailable);
                case ErrorCodes.PhotobankThumbnailAuthUnavailable:
                    return StatusCode(StatusCodes.Status503ServiceUnavailable);
                case ErrorCodes.PhotobankThumbnailUpstream:
                    return StatusCode(StatusCodes.Status502BadGateway);
                default:
                    return StatusCode(StatusCodes.Status502BadGateway);
            }
        }
```

- [ ] **Step 5: Fix the `using` directives**

At the top of `PhotobankController.cs`:

Remove these three lines (they are consumed only by the old `GetThumbnail` action):
```csharp
using System.Net.Http;
using Microsoft.Identity.Client;
using Anela.Heblo.Domain.Features.Photobank;
```

Add these two lines (keep the `using` block alphabetically grouped as the file already is):
```csharp
using Anela.Heblo.Application.Features.Photobank.UseCases.GetThumbnail;
using Anela.Heblo.Application.Shared;
```

> Keep `using Anela.Heblo.Application.Features.Photobank.Services;` — `ThumbnailSize` (the route parameter type) lives there and is still referenced. Keep `System.Collections.Generic`, `System.Threading`, `System.Threading.Tasks`, `Microsoft.AspNetCore.Http`, and `MediatR` — all still used by other actions.

- [ ] **Step 6: Run the controller tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankControllerThumbnailTests"`
Expected: PASS — 7 passed, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankControllerThumbnailTests.cs
git commit -m "refactor: reduce PhotobankController.GetThumbnail to thin MediatR dispatch"
```

---

### Task 5: Full verification

**Files:** none (verification only)

- [ ] **Step 1: Build the whole backend**

Run: `dotnet build Anela.Heblo.sln`
Expected: Build succeeded, 0 errors, 0 warnings introduced by these changes.

- [ ] **Step 2: Verify formatting**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: exit code 0 (no formatting changes needed). If it reports changes, run `dotnet format Anela.Heblo.sln`, review the diff (it must touch only the files in this plan), and amend the relevant commit.

- [ ] **Step 3: Run the full Photobank test slice**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Photobank"`
Expected: PASS — all Photobank tests green, including the 8 handler tests and 7 controller tests.

- [ ] **Step 4: Confirm no OpenAPI/TS client drift**

The action signature (route params + `IActionResult`) is unchanged and the new `GetThumbnailRequest`/`GetThumbnailResponse` types never appear in a controller method signature, so they do not enter the OpenAPI surface.

Run:
```bash
cd frontend && npm run build && cd ..
git status --porcelain frontend/src/api
```
Expected: `npm run build` succeeds; `git status` shows **no** changes under the generated API client directory. (A no-op regen diff, if any, is acceptable per the spec — but none is expected.)

- [ ] **Step 5: Final commit (only if Step 2 or 4 produced changes)**

```bash
git add -A
git commit -m "chore: formatting and generated-client sync for thumbnail use case"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (use case under folder convention; handler injects repo + graph + logger; absorbs logging; threads `CancellationToken`) → Tasks 2 & 3. Logging uses the same message templates and structured properties (`PhotoId`, `RetryAfter`, `ErrorCode`) as the original controller.
- FR-2 (controller = `_mediator.Send` + outcome→status; route + `[ProducesResponseType]` unchanged) → Task 4 Steps 3–5.
- FR-3 (single-dep `IMediator` constructor; drop unused usings) → Task 4 Steps 3 & 5; verified by Task 5 Steps 1–2.
- FR-4 (exact HTTP behaviour: 404 / 503+Retry-After / 503 auth / 502 / 200 + Cache-Control + Content-Length) → controller `switch` (Task 4 Step 4) + controller tests (Task 4 Step 1) + handler tests (Task 3 Step 1).
- FR-5 (controller tests mock `IMediator` only; new HTTP-free handler tests) → Tasks 3 & 4.
- NFR-1 (streaming, not buffering; immutable cache header) → response carries `Stream`; `Cache-Control` preserved verbatim.
- NFR-2 (no client-facing leakage; diagnostics logged server-side only) → only status codes returned; `ex.ErrorCode`/exception detail logged in handler.
- NFR-3 (stream lifecycle) → handler transfers `.Content` without disposing `GraphThumbnail`; guarded by `Content.CanRead` + `BeSameAs` assertions (handler) and `FileStream.Should().BeSameAs(stream)` (controller).
- NFR-4 (controller depends only on `IMediator`; no infra exception types in API project) → Task 4.
- Data Model: `GetThumbnailRequest` (`Id`, `Size`); `GetThumbnailResponse : BaseResponse` (`Content`, `ContentType`, `ContentLength`, `RetryAfterSeconds`) per the **arch-review's reversal** of spec Decision #2.
- Prerequisite (4 new `ErrorCodes`) → Task 1.

**Placeholder scan:** No TBD/TODO/"add error handling" placeholders — every code step contains complete code. The one no-op test helper (`SetupGraph`) is explicitly flagged for deletion in Task 3 Step 1.

**Type consistency:** `GetThumbnailRequest { int Id; ThumbnailSize Size; }`, `GetThumbnailResponse { Stream? Content; string? ContentType; long? ContentLength; int? RetryAfterSeconds; }`, and `ErrorCodes.PhotobankThumbnail{NotFound,Throttled,AuthUnavailable,Upstream}` are referenced identically across the handler, the controller, and both test files. The controller maps `PhotobankThumbnailUpstream → 502` and both throttle/auth → 503, matching the handler's classification and the FR-4 outcome table.
