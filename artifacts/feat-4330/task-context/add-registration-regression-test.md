### task: add-registration-regression-test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/API/ReprintExpeditionListHandlerRegistrationTests.cs`

This task adds a regression test that resolves `ReprintExpeditionListHandler` through a real DI container instead of constructing it directly. Written and run **before** the fix, it mechanically proves the bug: today there are two competing `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` registrations (MediatR's assembly scan plus `ExpeditionListArchiveModule`'s manual factory), so `GetServices<...>().Count()` is `2`, not `1`. The `remove-manual-handler-factory-and-inject-keyed-sink` task below makes this test pass by leaving exactly one registration in place.

- [ ] **Step 1: Write the regression test file**

```csharp
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
```

- [ ] **Step 2: Run the new tests and confirm the registration-count test fails against today's code**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReprintExpeditionListHandlerRegistrationTests"`

Expected: `ReprintExpeditionListHandler_HasExactlyOneRequestHandlerRegistration` **FAILS** (`Assert.Single` sees 2 items) -- this is the mechanical proof of the bug described in the issue. `KeyedCupsSinkRegistered_HandlerSendsThroughCupsSink` and `NoKeyedCupsSinkRegistered_HandlerFallsBackToAmbientSink` are expected to **PASS** already (today's manual factory implements the same keyed/fallback selection correctly; only the *extra, order-dependent* registration is the bug).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/API/ReprintExpeditionListHandlerRegistrationTests.cs
git commit -m "test(expedition-list-archive): add DI registration regression test for issue #4330

Proves the bug mechanically: resolving IRequestHandler<ReprintExpeditionListRequest,
ReprintExpeditionListResponse> through the real container currently returns 2
registrations (MediatR's assembly scan + ExpeditionListArchiveModule's manual
factory), not 1 -- an order-dependent last-wins collision. This test goes green
once the manual factory is removed in the next commit."
```

---

