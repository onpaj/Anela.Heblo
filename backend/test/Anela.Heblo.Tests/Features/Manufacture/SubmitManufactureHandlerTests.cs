using Anela.Heblo.Application.Features.Manufacture.Configuration;
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class SubmitManufactureHandlerTests
{
    private readonly Mock<IManufactureClient> _clientMock = new();
    private readonly Mock<IManufactureOrderRepository> _repositoryMock = new();
    private readonly Mock<IManufactureErrorTransformer> _transformerMock = new();
    private readonly Mock<ILogger<SubmitManufactureHandler>> _loggerMock = new();
    private readonly SubmitManufactureHandler _handler;

    public SubmitManufactureHandlerTests()
    {
        _handler = new SubmitManufactureHandler(
            _clientMock.Object,
            _repositoryMock.Object,
            _transformerMock.Object,
            TimeProvider.System,
            _loggerMock.Object,
            Options.Create(new ManufactureErpOptions()));
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

    [Fact]
    public async Task Handle_DoesNotActivateIngredientStockValidation()
    {
        // Arrange — ingredient stock validation must NOT be activated at the
        // client boundary by the default submit path. Activation should be an
        // explicit, documented opt-in in a separate PR.
        SubmitManufactureClientRequest? capturedClientRequest = null;
        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SubmitManufactureClientRequest, CancellationToken>(
                (r, _) => capturedClientRequest = r)
            .ReturnsAsync(new SubmitManufactureClientResponse { ManufactureId = "MAN-VAL-1" });

        // Act
        await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        capturedClientRequest.Should().NotBeNull();
        capturedClientRequest!.ValidateIngredientStock.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenErpTimesOut_PropagatesOperationCanceledException()
    {
        // Arrange — simulate Flexi taking longer than the configured timeout.
        // Use a very short timeout (1 ms) so the test doesn't actually wait.
        var handlerWithShortTimeout = new SubmitManufactureHandler(
            _clientMock.Object,
            _repositoryMock.Object,
            _transformerMock.Object,
            TimeProvider.System,
            _loggerMock.Object,
            Options.Create(new ManufactureErpOptions { ErpTimeoutSeconds = 1 }));

        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (SubmitManufactureClientRequest _, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return new SubmitManufactureClientResponse { ManufactureId = "NEVER" };
            });

        // Act
        var act = async () => await handlerWithShortTimeout.Handle(BuildRequest(), CancellationToken.None);

        // Assert — the timeout CTS cancels, which propagates as OperationCanceledException
        await act.Should().ThrowAsync<OperationCanceledException>();
        _transformerMock.Verify(t => t.Transform(It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTimeoutDisabled_DoesNotApplyCancelAfter()
    {
        // Arrange — ErpTimeoutSeconds = 0 means no application-level timeout.
        var handlerNoTimeout = new SubmitManufactureHandler(
            _clientMock.Object,
            _repositoryMock.Object,
            _transformerMock.Object,
            TimeProvider.System,
            _loggerMock.Object,
            Options.Create(new ManufactureErpOptions { ErpTimeoutSeconds = 0 }));

        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmitManufactureClientResponse { ManufactureId = "MAN-NOTIMEOUT" });

        // Act
        var result = await handlerNoTimeout.Handle(BuildRequest(), CancellationToken.None);

        // Assert — call succeeds; no timeout was enforced
        result.Success.Should().BeTrue();
        result.ManufactureId.Should().Be("MAN-NOTIMEOUT");
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

    // ── CTS timeout branch ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateLinkedCts_WhenErpTimeoutConfigured_CtsIsArmedWithTimeout()
    {
        // Arrange — use 30s timeout so the test does not race with CI wall-clock.
        CancellationToken capturedCt = default;
        var handlerWithTimeout = new SubmitManufactureHandler(
            _clientMock.Object,
            _repositoryMock.Object,
            _transformerMock.Object,
            TimeProvider.System,
            _loggerMock.Object,
            Options.Create(new ManufactureErpOptions { ErpTimeoutSeconds = 30 }));

        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SubmitManufactureClientRequest, CancellationToken>((_, ct) => capturedCt = ct)
            .ReturnsAsync(new SubmitManufactureClientResponse { ManufactureId = "MAN-CTS" });

        // Act
        await handlerWithTimeout.Handle(BuildRequest(), CancellationToken.None);

        // Assert — CancelAfter was called so the token can be canceled.
        capturedCt.CanBeCanceled.Should().BeTrue();
    }

    // ── PersistDocCodesAsync branches ─────────────────────────────────────────

    [Fact]
    public async Task PersistDocCodes_WhenOrderIdIsZero_RepositoryIsNotCalled()
    {
        // Arrange — BuildRequest() has ManufactureOrderId = 0 by default.
        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmitManufactureClientResponse { ManufactureId = "MAN-ZERO" });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.GetOrderByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PersistDocCodes_WhenOrderNotFound_LogsWarningAndDoesNotCallUpdate()
    {
        // Arrange
        var request = BuildRequest();
        request.ManufactureOrderId = 42;

        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmitManufactureClientResponse { ManufactureId = "MAN-NOTFOUND" });

        _repositoryMock
            .Setup(r => r.GetOrderByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("42")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _repositoryMock.Verify(
            r => r.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PersistDocCodes_WhenAllDocCodesPresent_WritesAllSixFieldsWithTimestamp()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
        var expectedNow = fakeTime.GetUtcNow().DateTime;

        var handlerWithFakeTime = new SubmitManufactureHandler(
            _clientMock.Object,
            _repositoryMock.Object,
            _transformerMock.Object,
            fakeTime,
            _loggerMock.Object,
            Options.Create(new ManufactureErpOptions()));

        var request = BuildRequest();
        request.ManufactureOrderId = 10;

        var order = new ManufactureOrder { Id = 10, OrderNumber = "MO-010", StateChangedByUser = "test" };

        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmitManufactureClientResponse
            {
                ManufactureId = "MAN-ALL",
                MaterialIssueForSemiProductDocCode = "V-MAT-001",
                SemiProductReceiptDocCode = "V-POL-001",
                SemiProductIssueForProductDocCode = "V-POLV-001",
                MaterialIssueForProductDocCode = "V-MATV-001",
                ProductReceiptDocCode = "V-PRIJEM-001",
                DirectSemiProductOutputDocCode = "V-DIRECT-001",
            });

        _repositoryMock
            .Setup(r => r.GetOrderByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _repositoryMock
            .Setup(r => r.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        // Act
        var result = await handlerWithFakeTime.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        order.DocMaterialIssueForSemiProduct.Should().Be("V-MAT-001");
        order.DocMaterialIssueForSemiProductDate.Should().Be(expectedNow);
        order.DocSemiProductReceipt.Should().Be("V-POL-001");
        order.DocSemiProductReceiptDate.Should().Be(expectedNow);
        order.DocSemiProductIssueForProduct.Should().Be("V-POLV-001");
        order.DocSemiProductIssueForProductDate.Should().Be(expectedNow);
        order.DocMaterialIssueForProduct.Should().Be("V-MATV-001");
        order.DocMaterialIssueForProductDate.Should().Be(expectedNow);
        order.DocProductReceipt.Should().Be("V-PRIJEM-001");
        order.DocProductReceiptDate.Should().Be(expectedNow);
        order.ErpDiscardResidueDocumentNumber.Should().Be("V-DIRECT-001");
        order.ErpDiscardResidueDocumentNumberDate.Should().Be(expectedNow);

        _repositoryMock.Verify(
            r => r.UpdateOrderAsync(order, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PersistDocCodes_WhenSomeDocCodesNull_WritesOnlyNonNullFields()
    {
        // Arrange — only MaterialIssueForSemiProductDocCode and ProductReceiptDocCode are set.
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
        var expectedNow = fakeTime.GetUtcNow().DateTime;

        var handlerWithFakeTime = new SubmitManufactureHandler(
            _clientMock.Object,
            _repositoryMock.Object,
            _transformerMock.Object,
            fakeTime,
            _loggerMock.Object,
            Options.Create(new ManufactureErpOptions()));

        var request = BuildRequest();
        request.ManufactureOrderId = 20;

        var order = new ManufactureOrder { Id = 20, OrderNumber = "MO-020", StateChangedByUser = "test" };

        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmitManufactureClientResponse
            {
                ManufactureId = "MAN-PARTIAL",
                MaterialIssueForSemiProductDocCode = "V-MAT-PARTIAL",
                SemiProductReceiptDocCode = null,
                SemiProductIssueForProductDocCode = null,
                MaterialIssueForProductDocCode = null,
                ProductReceiptDocCode = "V-PRIJEM-PARTIAL",
                DirectSemiProductOutputDocCode = null,
            });

        _repositoryMock
            .Setup(r => r.GetOrderByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _repositoryMock
            .Setup(r => r.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        // Act
        var result = await handlerWithFakeTime.Handle(request, CancellationToken.None);

        // Assert — two non-null fields written
        result.Success.Should().BeTrue();
        order.DocMaterialIssueForSemiProduct.Should().Be("V-MAT-PARTIAL");
        order.DocMaterialIssueForSemiProductDate.Should().Be(expectedNow);
        order.DocProductReceipt.Should().Be("V-PRIJEM-PARTIAL");
        order.DocProductReceiptDate.Should().Be(expectedNow);

        // Assert — four null fields left untouched
        order.DocSemiProductReceipt.Should().BeNull();
        order.DocSemiProductReceiptDate.Should().BeNull();
        order.DocSemiProductIssueForProduct.Should().BeNull();
        order.DocSemiProductIssueForProductDate.Should().BeNull();
        order.DocMaterialIssueForProduct.Should().BeNull();
        order.DocMaterialIssueForProductDate.Should().BeNull();
        order.ErpDiscardResidueDocumentNumber.Should().BeNull();
        order.ErpDiscardResidueDocumentNumberDate.Should().BeNull();

        _repositoryMock.Verify(
            r => r.UpdateOrderAsync(order, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
