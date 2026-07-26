### task: add-container-name-validator-and-pipeline-wiring

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Validators/DownloadFromUrlRequestValidatorTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Pipeline/FileStorageValidationPipelineTests.cs`

This task adds the new validator, its DI/pipeline wiring, and its tests in one buildable commit. The handler's existing inline `IsValidContainerName` check is left untouched here (removed in the next task) — for the duration of this commit the same rule is enforced twice (redundant, harmless), which keeps every commit in this plan independently green.

- [ ] **Step 1: Create the validator**

Create `backend/src/Anela.Heblo.Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs`:

```csharp
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Shared;
using FluentValidation;

namespace Anela.Heblo.Application.Features.FileStorage.Validators;

public class DownloadFromUrlRequestValidator : AbstractValidator<DownloadFromUrlRequest>
{
    public DownloadFromUrlRequestValidator()
    {
        RuleFor(x => x.ContainerName)
            .Must(IsValidContainerName)
            .WithErrorCode(((int)ErrorCodes.InvalidContainerName).ToString())
            .WithState(x => (object)new Dictionary<string, string>
            {
                { "containerName", x.ContainerName },
                { "cause", "validation" },
            })
            .WithMessage("Invalid container name");
    }

    private static bool IsValidContainerName(string containerName)
    {
        if (string.IsNullOrEmpty(containerName) || containerName.Length < 3 || containerName.Length > 63)
            return false;

        if (containerName != containerName.ToLowerInvariant())
            return false;

        if (!char.IsLetterOrDigit(containerName[0]) || !char.IsLetterOrDigit(containerName[^1]))
            return false;

        for (int i = 0; i < containerName.Length; i++)
        {
            var c = containerName[i];
            if (!char.IsLetterOrDigit(c) && c != '-')
                return false;

            if (c == '-' && i < containerName.Length - 1 && containerName[i + 1] == '-')
                return false;
        }

        return true;
    }
}
```

- [ ] **Step 2: Register the validator and pipeline behavior in `FileStorageModule`**

In `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`, add three `using` directives at the top (after the existing `using System.Net;` line, keeping the rest as-is):

```csharp
using System.Net;
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Features.FileStorage.Validators;
using Anela.Heblo.Domain.Features.FileStorage;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
```

Then, immediately before the final `return services;` in `AddFileStorageModule` (right after the existing `services.Configure<FileDownloadOptions>(configuration.GetSection("FileStorage:Download"));` line), add:

```csharp
        // Register validator + pipeline behavior for DownloadFromUrlRequest, mirroring
        // AnalyticsModule's ValidationResultBehavior wiring (non-throwing, reconstructs
        // the response's own Success/ErrorCode/Params contract instead of throwing).
        services.AddScoped<IValidator<DownloadFromUrlRequest>, DownloadFromUrlRequestValidator>();
        services.AddScoped<IPipelineBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>,
            ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>>();

        return services;
```

The full method body's tail should now read:

```csharp
        services.Configure<FileDownloadOptions>(configuration.GetSection("FileStorage:Download"));

        // Register validator + pipeline behavior for DownloadFromUrlRequest, mirroring
        // AnalyticsModule's ValidationResultBehavior wiring (non-throwing, reconstructs
        // the response's own Success/ErrorCode/Params contract instead of throwing).
        services.AddScoped<IValidator<DownloadFromUrlRequest>, DownloadFromUrlRequestValidator>();
        services.AddScoped<IPipelineBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>,
            ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>>();

        return services;
    }
}
```

- [ ] **Step 3: Create validator unit tests**

Create `backend/test/Anela.Heblo.Tests/Features/FileStorage/Validators/DownloadFromUrlRequestValidatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Features.FileStorage.Validators;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage.Validators;

public class DownloadFromUrlRequestValidatorTests
{
    private readonly DownloadFromUrlRequestValidator _validator;

    public DownloadFromUrlRequestValidatorTests()
    {
        _validator = new DownloadFromUrlRequestValidator();
    }

    private static DownloadFromUrlRequest CreateRequest(string containerName) =>
        new()
        {
            FileUrl = "https://example.com/file.txt",
            ContainerName = containerName,
        };

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("very-long-container-name-that-exceeds-sixty-three-characters-limit")]
    [InlineData("InvalidCase")]
    [InlineData("invalid--double-hyphen")]
    [InlineData("-starts-with-hyphen")]
    [InlineData("ends-with-hyphen-")]
    [InlineData("invalid_underscore")]
    public void ContainerName_Invalid_ShouldHaveValidationError(string invalidContainerName)
    {
        // Arrange
        var request = CreateRequest(invalidContainerName);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ContainerName)
            .WithErrorMessage("Invalid container name");
    }

    [Theory]
    [InlineData("valid-container")]
    [InlineData("container123")]
    [InlineData("my-container-name")]
    [InlineData("abc")]
    [InlineData("container-with-exactly-sixty-three-characters-in-total-length")]
    public void ContainerName_Valid_ShouldNotHaveValidationError(string validContainerName)
    {
        // Arrange
        var request = CreateRequest(validContainerName);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.ContainerName);
    }

    [Fact]
    public void ContainerName_Invalid_ShouldHaveInvalidContainerNameErrorCode()
    {
        // Arrange
        var request = CreateRequest("INVALID_UPPERCASE");

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var failure = result.Errors.Single(f => f.PropertyName == nameof(DownloadFromUrlRequest.ContainerName));
        failure.ErrorCode.Should().Be(((int)ErrorCodes.InvalidContainerName).ToString());
    }

    [Fact]
    public void ContainerName_Invalid_ErrorCodeRoundTrips_ToInvalidContainerName()
    {
        // Arrange
        var request = CreateRequest("INVALID_UPPERCASE");

        // Act
        var result = _validator.TestValidate(request);
        var failure = result.Errors.Single(f => f.PropertyName == nameof(DownloadFromUrlRequest.ContainerName));

        // Assert — the WithErrorCode string must round-trip through Enum.TryParse<ErrorCodes>
        // exactly to ErrorCodes.InvalidContainerName, per spec FR-1's acceptance criteria.
        var parsed = Enum.TryParse<ErrorCodes>(failure.ErrorCode, out var errorCode);
        parsed.Should().BeTrue();
        errorCode.Should().Be(ErrorCodes.InvalidContainerName);
    }

    [Fact]
    public void ContainerName_Invalid_ShouldHaveCorrectParams()
    {
        // Arrange
        var request = CreateRequest("INVALID_UPPERCASE");

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var failure = result.Errors.Single(f => f.PropertyName == nameof(DownloadFromUrlRequest.ContainerName));
        var customState = failure.CustomState as Dictionary<string, string>;
        customState.Should().NotBeNull();
        customState.Should().ContainKey("containerName").WhoseValue.Should().Be("INVALID_UPPERCASE");
        customState.Should().ContainKey("cause").WhoseValue.Should().Be("validation");
    }
}
```

- [ ] **Step 4: Create the end-to-end DI/MediatR pipeline test**

Create `backend/test/Anela.Heblo.Tests/Features/FileStorage/Pipeline/FileStorageValidationPipelineTests.cs`:

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Features.FileStorage.Validators;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.FileStorage;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage.Pipeline;

/// <summary>
/// Integration tests for the FileStorage validation pipeline behavior.
/// Verifies that ValidationResultBehavior + DownloadFromUrlRequestValidator are wired
/// correctly (mirroring AnalyticsModule's DI pattern), so an invalid container name
/// short-circuits before DownloadFromUrlHandler.Handle executes, and a valid one reaches it.
/// </summary>
public class FileStorageValidationPipelineTests
{
    private static IMediator BuildMediator(
        Mock<IBlobStorageService> blobStorage,
        Mock<IDownloadResilienceService> resilience)
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DownloadFromUrlHandler).Assembly));

        services.AddScoped<IValidator<DownloadFromUrlRequest>, DownloadFromUrlRequestValidator>();
        services.AddScoped<IPipelineBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>,
            ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>>();

        services.AddScoped(_ => blobStorage.Object);
        services.AddScoped(_ => resilience.Object);
        services.AddSingleton(BuildHeadFactory());
        services.AddSingleton<IOptions<FileDownloadOptions>>(
            Options.Create(new FileDownloadOptions { HeadTimeout = TimeSpan.FromSeconds(5) }));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static IHttpClientFactory BuildHeadFactory()
    {
        var handler = new StubHttpMessageHandler();
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return factory.Object;
    }

    [Fact]
    public async Task Send_InvalidContainerName_ShortCircuits_BlobStorageNeverInvoked()
    {
        // Arrange
        var blobStorage = new Mock<IBlobStorageService>();
        var resilience = new Mock<IDownloadResilienceService>();
        var mediator = BuildMediator(blobStorage, resilience);

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/file.txt",
            ContainerName = "INVALID_UPPERCASE",
        };

        // Act
        var result = await mediator.Send(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidContainerName);
        result.Params.Should().ContainKey("containerName").WhoseValue.Should().Be("INVALID_UPPERCASE");
        result.Params.Should().ContainKey("cause").WhoseValue.Should().Be("validation");
        blobStorage.Verify(
            s => s.DownloadFromUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Send_ValidContainerName_ReachesHandler_ReturnsSuccess()
    {
        // Arrange
        var blobStorage = new Mock<IBlobStorageService>();
        blobStorage
            .Setup(s => s.DownloadFromUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://mock.blob.core.windows.net/documents/file.txt");

        var resilience = new Mock<IDownloadResilienceService>();
        resilience
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<string>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<string>>, string, CancellationToken>(
                (op, _, ct) => op(ct));

        var mediator = BuildMediator(blobStorage, resilience);

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/file.txt",
            ContainerName = "documents",
        };

        // Act
        var result = await mediator.Send(request);

        // Assert
        result.Success.Should().BeTrue();
        result.ContainerName.Should().Be("documents");
        blobStorage.Verify(
            s => s.DownloadFromUrlAsync(
                It.IsAny<string>(), "documents", It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
            return Task.FromResult(response);
        }
    }
}
```

- [ ] **Step 5: Build and run the new/changed tests**

```bash
cd backend
dotnet build
dotnet test Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileStorage.Validators.DownloadFromUrlRequestValidatorTests|FullyQualifiedName~FileStorage.Pipeline.FileStorageValidationPipelineTests"
```

Confirm: build succeeds with no new warnings; both new test classes pass in full (all `[Theory]` cases and the two pipeline `[Fact]`s green). The existing `DownloadFromUrlHandlerTests.cs` must still pass unchanged at this point too (handler still has its own inline check, now duplicated by the validator — harmless).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs \
        backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs \
        backend/test/Anela.Heblo.Tests/Features/FileStorage/Validators/DownloadFromUrlRequestValidatorTests.cs \
        backend/test/Anela.Heblo.Tests/Features/FileStorage/Pipeline/FileStorageValidationPipelineTests.cs
git commit -m "Add DownloadFromUrlRequestValidator wired via ValidationResultBehavior

Extracts the Azure container-naming rule into a FluentValidation
validator, registered in FileStorageModule alongside the existing
ValidationResultBehavior pipeline (mirrors AnalyticsModule's pattern).
The handler's own inline check is removed in a follow-up commit."
```

---
