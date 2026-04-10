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
    public async Task Handle_WhenClientSucceeds_PropagatesAllFlexiDocCodes()
    {
        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmitManufactureClientResponse
            {
                ManufactureId = "MAN-001",
                MaterialIssueForSemiProductDocCode = "V-MAT-001",
                SemiProductReceiptDocCode = "V-POL-001",
                SemiProductIssueForProductDocCode = "V-POLV-001",
                MaterialIssueForProductDocCode = "V-MATV-001",
                ProductReceiptDocCode = "V-PRIJEM-001",
            });

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ManufactureId.Should().Be("MAN-001");
        result.MaterialIssueForSemiProductDocCode.Should().Be("V-MAT-001");
        result.SemiProductReceiptDocCode.Should().Be("V-POL-001");
        result.SemiProductIssueForProductDocCode.Should().Be("V-POLV-001");
        result.MaterialIssueForProductDocCode.Should().Be("V-MATV-001");
        result.ProductReceiptDocCode.Should().Be("V-PRIJEM-001");
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
