using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class SubmitManufactureHandlerTests
{
    private readonly Mock<IManufactureClient> _clientMock = new();
    private readonly Mock<IManufactureErrorTransformer> _transformerMock = new();
    private readonly Mock<ILogger<SubmitManufactureHandler>> _loggerMock = new();
    private readonly SubmitManufactureHandler _handler;

    public SubmitManufactureHandlerTests()
    {
        _handler = new SubmitManufactureHandler(
            _clientMock.Object,
            _transformerMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenClientSucceeds_ReturnsSuccessResponse()
    {
        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmitManufactureClientResponse { ManufactureId = "MAN-001" });

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ManufactureId.Should().Be("MAN-001");
        result.UserMessage.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenClientThrows_SetsUserMessageFromTransformer()
    {
        var ex = new InvalidOperationException("Flexi raw error");
        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);
        _transformerMock
            .Setup(t => t.Transform(ex))
            .Returns("Nedostatek meziproduktu 'XYZ' na skladu POLOTOVARY.");

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.UserMessage.Should().Be("Nedostatek meziproduktu 'XYZ' na skladu POLOTOVARY.");
    }

    [Fact]
    public async Task Handle_WhenClientThrows_LogsOriginalException()
    {
        var ex = new InvalidOperationException("Flexi raw error");
        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);
        _transformerMock.Setup(t => t.Transform(It.IsAny<Exception>())).Returns("any message");

        await _handler.Handle(BuildRequest(), CancellationToken.None);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                ex,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_MapsAllFieldsToClientRequest()
    {
        SubmitManufactureClientRequest? captured = null;
        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SubmitManufactureClientRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new SubmitManufactureClientResponse { ManufactureId = "MAN-999" });

        var expirationDate = new DateOnly(2027, 6, 30);
        var date = new DateTime(2026, 4, 9, 10, 0, 0, DateTimeKind.Utc);
        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = "MO-MAPPED",
            ManufactureInternalNumber = "INT-MAPPED",
            ManufactureType = ErpManufactureType.SemiProduct,
            Date = date,
            CreatedBy = "mapper@anela.cz",
            LotNumber = "LOT-001",
            ExpirationDate = expirationDate,
            Items = new List<SubmitManufactureRequestItem>
            {
                new() { ProductCode = "PROD-X", Name = "Product X", Amount = 50 }
            }
        };

        await _handler.Handle(request, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ManufactureOrderCode.Should().Be("MO-MAPPED");
        captured.ManufactureInternalNumber.Should().Be("INT-MAPPED");
        captured.ManufactureType.Should().Be(ErpManufactureType.SemiProduct);
        captured.Date.Should().Be(date);
        captured.CreatedBy.Should().Be("mapper@anela.cz");
        captured.LotNumber.Should().Be("LOT-001");
        captured.ExpirationDate.Should().Be(expirationDate);
        captured.Items.Should().HaveCount(1);
        captured.Items[0].ProductCode.Should().Be("PROD-X");
        captured.Items[0].ProductName.Should().Be("Product X");
        captured.Items[0].Amount.Should().Be(50);
    }

    [Fact]
    public async Task Handle_WhenCancelled_PropagatesOperationCanceledException()
    {
        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = async () => await _handler.Handle(BuildRequest(), new CancellationTokenSource().Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _transformerMock.Verify(t => t.Transform(It.IsAny<Exception>()), Times.Never);
    }

    private static SubmitManufactureRequest BuildRequest() => new()
    {
        ManufactureOrderNumber = "MO-001",
        ManufactureInternalNumber = "INT-001",
        ManufactureType = ErpManufactureType.SemiProduct,
        Date = DateTime.UtcNow,
        CreatedBy = "test@anela.cz",
        Items = new List<SubmitManufactureRequestItem>
        {
            new() { ProductCode = "PROD001", Name = "Test Product", Amount = 100 }
        }
    };
}
