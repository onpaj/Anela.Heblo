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
    public async Task Send_InvalidFileUrlAndInvalidContainerName_ReturnsInvalidUrlFormat()
    {
        // Arrange
        var blobStorage = new Mock<IBlobStorageService>();
        var resilience = new Mock<IDownloadResilienceService>();
        var mediator = BuildMediator(blobStorage, resilience);

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "not-a-url",
            ContainerName = "AB",
        };

        // Act
        var result = await mediator.Send(request);

        // Assert — pre-refactor precedence: URL-format error wins over container-name error
        // when both are invalid (see FileStorageValidator rule ordering).
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidUrlFormat);
        result.Params.Should().ContainKey("fileUrl").WhoseValue.Should().Be("not-a-url");
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
