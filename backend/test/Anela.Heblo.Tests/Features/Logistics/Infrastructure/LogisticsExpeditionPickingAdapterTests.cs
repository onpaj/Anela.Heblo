using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.Logistics.Infrastructure;
using Anela.Heblo.Application.Features.Logistics.Picking;
using Anela.Heblo.Domain.Features.Logistics;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Infrastructure;

public class LogisticsExpeditionPickingAdapterTests
{
    private readonly Mock<IPickingListSource> _innerSource = new();

    private LogisticsExpeditionPickingAdapter CreateAdapter() =>
        new LogisticsExpeditionPickingAdapter(_innerSource.Object);

    [Fact]
    public async Task CreatePickingListAsync_TranslatesRequestFieldsOneToOne()
    {
        // Arrange
        PrintPickingListRequest? captured = null;
        _innerSource
            .Setup(x => x.CreatePickingList(
                It.IsAny<PrintPickingListRequest>(),
                It.IsAny<Func<IList<string>, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Callback((PrintPickingListRequest req, Func<IList<string>, Task>? _, CancellationToken __) => captured = req)
            .ReturnsAsync(new PrintPickingListResult());

        var request = new ExpeditionPickingRequest
        {
            Carriers = new List<Carriers> { Carriers.GLS, Carriers.PPL },
            SourceStateId = 7,
            DesiredStateId = 42,
            ChangeOrderState = true,
            SendToPrinter = true,
        };

        // Act
        await CreateAdapter().CreatePickingListAsync(request, onBatchFilesReady: null);

        // Assert
        captured.Should().NotBeNull();
        captured!.Carriers.Should().BeEquivalentTo(new[] { Carriers.GLS, Carriers.PPL });
        captured.SourceStateId.Should().Be(7);
        captured.DesiredStateId.Should().Be(42);
        captured.ChangeOrderState.Should().BeTrue();
        captured.SendToPrinter.Should().BeTrue();
    }

    [Fact]
    public async Task CreatePickingListAsync_TranslatesResultFields()
    {
        // Arrange
        var innerResult = new PrintPickingListResult
        {
            ExportedFiles = new List<string> { "/tmp/a.pdf", "/tmp/b.pdf" },
            TotalCount = 12,
        };
        _innerSource
            .Setup(x => x.CreatePickingList(
                It.IsAny<PrintPickingListRequest>(),
                It.IsAny<Func<IList<string>, Task>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(innerResult);

        // Act
        var result = await CreateAdapter().CreatePickingListAsync(
            new ExpeditionPickingRequest(), onBatchFilesReady: null);

        // Assert
        result.ExportedFiles.Should().BeEquivalentTo(new[] { "/tmp/a.pdf", "/tmp/b.pdf" });
        result.TotalCount.Should().Be(12);
    }

    [Fact]
    public async Task CreatePickingListAsync_PassesCallbackThroughVerbatim()
    {
        // Arrange
        Func<IList<string>, Task>? forwarded = null;
        _innerSource
            .Setup(x => x.CreatePickingList(
                It.IsAny<PrintPickingListRequest>(),
                It.IsAny<Func<IList<string>, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Callback((PrintPickingListRequest _, Func<IList<string>, Task>? cb, CancellationToken __) => forwarded = cb)
            .ReturnsAsync(new PrintPickingListResult());

        var invoked = 0;
        Func<IList<string>, Task> outerCallback = _ => { invoked++; return Task.CompletedTask; };

        // Act
        await CreateAdapter().CreatePickingListAsync(
            new ExpeditionPickingRequest(), outerCallback);

        // Assert
        forwarded.Should().BeSameAs(outerCallback);
        await forwarded!(new List<string>());
        invoked.Should().Be(1);
    }

    [Fact]
    public async Task CreatePickingListAsync_PassesCancellationTokenThrough()
    {
        // Arrange
        CancellationToken captured = default;
        _innerSource
            .Setup(x => x.CreatePickingList(
                It.IsAny<PrintPickingListRequest>(),
                It.IsAny<Func<IList<string>, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Callback((PrintPickingListRequest _, Func<IList<string>, Task>? __, CancellationToken ct) => captured = ct)
            .ReturnsAsync(new PrintPickingListResult());

        using var cts = new CancellationTokenSource();

        // Act
        await CreateAdapter().CreatePickingListAsync(
            new ExpeditionPickingRequest(), onBatchFilesReady: null, cts.Token);

        // Assert
        captured.Should().Be(cts.Token);
    }
}
