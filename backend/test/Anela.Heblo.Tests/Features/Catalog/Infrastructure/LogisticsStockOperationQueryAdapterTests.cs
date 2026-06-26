using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class LogisticsStockOperationQueryAdapterTests
{
    private readonly Mock<IStockUpOperationRepository> _repository = new();

    private LogisticsStockOperationQueryAdapter CreateAdapter() => new(_repository.Object);

    private static StockUpOperation CreateOperation(
        int id,
        string documentNumber,
        StockUpOperationState state)
    {
        var operation = new StockUpOperation(
            documentNumber,
            "PROD-001",
            1,
            StockUpSourceType.TransportBox,
            1);

        typeof(StockUpOperation).GetProperty("Id")!.SetValue(operation, id);
        typeof(StockUpOperation).GetProperty("State")!.SetValue(operation, state);
        return operation;
    }

    private void SetupRepositoryReturns(StockUpSourceType sourceType, int sourceId, List<StockUpOperation> operations)
    {
        _repository
            .Setup(r => r.GetBySourceAsync(sourceType, sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations);
    }

    [Fact]
    public async Task GetOperationsBySourceAsync_WithTransportBoxSource_CallsRepositoryWithMappedEnum()
    {
        SetupRepositoryReturns(StockUpSourceType.TransportBox, 42, new List<StockUpOperation>());

        await CreateAdapter().GetOperationsBySourceAsync(
            LogisticsStockOperationSource.TransportBox, 42);

        _repository.Verify(
            r => r.GetBySourceAsync(StockUpSourceType.TransportBox, 42, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOperationsBySourceAsync_WithGiftPackageManufactureSource_CallsRepositoryWithMappedEnum()
    {
        SetupRepositoryReturns(StockUpSourceType.GiftPackageManufacture, 7, new List<StockUpOperation>());

        await CreateAdapter().GetOperationsBySourceAsync(
            LogisticsStockOperationSource.GiftPackageManufacture, 7);

        _repository.Verify(
            r => r.GetBySourceAsync(StockUpSourceType.GiftPackageManufacture, 7, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOperationsBySourceAsync_WithUnknownSource_ThrowsArgumentOutOfRangeException()
    {
        var unknownSource = (LogisticsStockOperationSource)999;

        var act = () => CreateAdapter().GetOperationsBySourceAsync(unknownSource, 1);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(StockUpOperationState.Pending, LogisticsStockOperationState.Pending)]
    [InlineData(StockUpOperationState.Submitted, LogisticsStockOperationState.Submitted)]
    [InlineData(StockUpOperationState.Completed, LogisticsStockOperationState.Completed)]
    [InlineData(StockUpOperationState.Failed, LogisticsStockOperationState.Failed)]
    public async Task GetOperationsBySourceAsync_MapsStateOneToOne(
        StockUpOperationState catalogState,
        LogisticsStockOperationState expectedLogisticsState)
    {
        SetupRepositoryReturns(
            StockUpSourceType.TransportBox,
            1,
            new List<StockUpOperation> { CreateOperation(1, "DOC-1", catalogState) });

        var result = await CreateAdapter().GetOperationsBySourceAsync(
            LogisticsStockOperationSource.TransportBox, 1);

        result.Should().ContainSingle()
            .Which.State.Should().Be(expectedLogisticsState);
    }

    [Fact]
    public async Task GetOperationsBySourceAsync_ProjectsDocumentNumber()
    {
        SetupRepositoryReturns(
            StockUpSourceType.TransportBox,
            1,
            new List<StockUpOperation>
            {
                CreateOperation(1, "DOC-A", StockUpOperationState.Completed),
                CreateOperation(2, "DOC-B", StockUpOperationState.Pending),
            });

        var result = await CreateAdapter().GetOperationsBySourceAsync(
            LogisticsStockOperationSource.TransportBox, 1);

        result.Select(s => s.DocumentNumber).Should().BeEquivalentTo(new[] { "DOC-A", "DOC-B" });
    }

    [Fact]
    public async Task GetOperationsBySourceAsync_WhenRepositoryEmpty_ReturnsEmptyList()
    {
        SetupRepositoryReturns(StockUpSourceType.TransportBox, 1, new List<StockUpOperation>());

        var result = await CreateAdapter().GetOperationsBySourceAsync(
            LogisticsStockOperationSource.TransportBox, 1);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOperationsBySourceAsync_HandlesEveryCatalogStateMember_WithoutThrowing()
    {
        // Enum-parity guard: if Catalog adds a new StockUpOperationState member (e.g. Reconciling),
        // this test fails before production traffic hits the adapter's exhaustive switch.
        foreach (var state in Enum.GetValues<StockUpOperationState>())
        {
            _repository.Reset();
            SetupRepositoryReturns(
                StockUpSourceType.TransportBox,
                1,
                new List<StockUpOperation> { CreateOperation(1, "DOC-1", state) });

            var act = () => CreateAdapter().GetOperationsBySourceAsync(
                LogisticsStockOperationSource.TransportBox, 1);

            await act.Should().NotThrowAsync(
                $"adapter must map Catalog state {state} to a Logistics state");
        }
    }

    [Fact]
    public async Task GetOperationsBySourceAsync_PropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        SetupRepositoryReturns(StockUpSourceType.TransportBox, 1, new List<StockUpOperation>());

        await CreateAdapter().GetOperationsBySourceAsync(
            LogisticsStockOperationSource.TransportBox, 1, cts.Token);

        _repository.Verify(
            r => r.GetBySourceAsync(StockUpSourceType.TransportBox, 1, cts.Token),
            Times.Once);
    }
}
