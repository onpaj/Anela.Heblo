using Anela.Heblo.Application.Features.Manufacture.Infrastructure;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Infrastructure;

public class ManufactureGiftPackageBomAdapterTests
{
    private readonly Mock<IManufactureClient> _clientMock = new();
    private readonly ManufactureGiftPackageBomAdapter _adapter;

    public ManufactureGiftPackageBomAdapterTests()
    {
        _adapter = new ManufactureGiftPackageBomAdapter(_clientMock.Object);
    }

    [Fact]
    public async Task GetSetPartsAsync_MapsPartsPreservingOrderAndFields()
    {
        _clientMock
            .Setup(c => c.GetSetPartsAsync("SET1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPart>
            {
                new() { ProductCode = "A", ProductName = "Part A", Amount = 2 },
                new() { ProductCode = "B", ProductName = "Part B", Amount = 0.5 },
            });

        var result = await _adapter.GetSetPartsAsync("SET1");

        result.Should().HaveCount(2);
        result[0].ProductCode.Should().Be("A");
        result[0].ProductName.Should().Be("Part A");
        result[0].Amount.Should().Be(2);
        result[1].ProductCode.Should().Be("B");
        result[1].ProductName.Should().Be("Part B");
        result[1].Amount.Should().Be(0.5);
    }

    [Fact]
    public async Task GetSetPartsAsync_EmptyClientResult_ReturnsEmptyList()
    {
        _clientMock
            .Setup(c => c.GetSetPartsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPart>());

        var result = await _adapter.GetSetPartsAsync("SET1");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSetPartsAsync_ClientThrows_PropagatesException()
    {
        _clientMock
            .Setup(c => c.GetSetPartsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var act = () => _adapter.GetSetPartsAsync("SET1");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task GetSetPartsAsync_ForwardsCodeAndCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        _clientMock
            .Setup(c => c.GetSetPartsAsync("SET9", cts.Token))
            .ReturnsAsync(new List<ProductPart>());

        await _adapter.GetSetPartsAsync("SET9", cts.Token);

        _clientMock.Verify(c => c.GetSetPartsAsync("SET9", cts.Token), Times.Once);
    }
}
