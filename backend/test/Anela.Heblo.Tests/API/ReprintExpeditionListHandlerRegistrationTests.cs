using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using Anela.Heblo.Application.Shared.Printing;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.API;

public class ReprintExpeditionListHandlerRegistrationTests
{
    private static ServiceProvider BuildProvider(
        bool registerKeyedCupsSink,
        out Mock<IPrintQueueSink> cupsSinkMock,
        out Mock<IPrintQueueSink> fallbackSinkMock)
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();

        // Mirrors the production composition path: MediatR's assembly scan registers every
        // handler in the Application assembly, then ExpeditionListArchiveModule is registered.
        // This test deliberately does NOT depend on which of these two calls comes first --
        // that is exactly the invariant this fix removes.
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ReprintExpeditionListHandler>());
        services.AddExpeditionListArchiveModule(configuration);

        cupsSinkMock = new Mock<IPrintQueueSink>();
        cupsSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fallbackSinkMock = new Mock<IPrintQueueSink>();
        fallbackSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Minimal, network-free stand-ins for the keyed "cups" slot and the ambient
        // (non-keyed) IPrintQueueSink -- the same two slots AddPrintQueueSink wires up in
        // production, without pulling in the real CUPS/FileSystem/Azure adapters.
        if (registerKeyedCupsSink)
        {
            services.AddKeyedSingleton<IPrintQueueSink>("cups", cupsSinkMock.Object);
        }
        services.AddSingleton<IPrintQueueSink>(fallbackSinkMock.Object);

        var blobStoreMock = new Mock<IExpeditionListArchiveBlobStore>();
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        blobStoreMock
            .Setup(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(pdfBytes));
        services.AddSingleton(blobStoreMock.Object);

        var temporaryFileAccessorMock = new Mock<ITemporaryFileAccessor>();
        temporaryFileAccessorMock
            .Setup(a => a.CreateFromStreamAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/tmp/registration-test.pdf");
        services.AddSingleton(temporaryFileAccessorMock.Object);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void ReprintExpeditionListHandler_HasExactlyOneRequestHandlerRegistration()
    {
        // Arrange -- regression guard for the manual IRequestHandler factory this fix removes.
        // Before the fix, both MediatR's assembly scan and ExpeditionListArchiveModule's manual
        // factory register IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>,
        // so this count is 2 and the *last* registration silently wins -- entirely dependent on
        // the order AddMediatR and AddExpeditionListArchiveModule are called in.
        using var provider = BuildProvider(registerKeyedCupsSink: true, out _, out _);

        // Act
        var handlers = provider
            .GetServices<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>()
            .ToList();

        // Assert
        Assert.Single(handlers);
    }

    [Fact]
    public async Task KeyedCupsSinkRegistered_HandlerSendsThroughCupsSink()
    {
        // Arrange
        using var provider = BuildProvider(registerKeyedCupsSink: true, out var cupsSinkMock, out var fallbackSinkMock);
        var handler = provider
            .GetRequiredService<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>();

        // Act
        var result = await handler.Handle(
            new ReprintExpeditionListRequest { BlobPath = "2026-03-25/picking-list-001.pdf" }, default);

        // Assert
        Assert.True(result.Success);
        cupsSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        fallbackSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoKeyedCupsSinkRegistered_HandlerFallsBackToAmbientSink()
    {
        // Arrange
        using var provider = BuildProvider(registerKeyedCupsSink: false, out var cupsSinkMock, out var fallbackSinkMock);
        var handler = provider
            .GetRequiredService<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>();

        // Act
        var result = await handler.Handle(
            new ReprintExpeditionListRequest { BlobPath = "2026-03-25/picking-list-002.pdf" }, default);

        // Assert
        Assert.True(result.Success);
        fallbackSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        cupsSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
