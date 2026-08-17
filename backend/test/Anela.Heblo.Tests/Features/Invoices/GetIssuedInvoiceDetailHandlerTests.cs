using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

/// <summary>
/// Pins validation behavior in GetIssuedInvoiceDetailHandler that was previously
/// duplicated in the now-deleted IssuedInvoicesController.
/// </summary>
public class GetIssuedInvoiceDetailHandlerTests
{
    private readonly Mock<IIssuedInvoiceRepository> _repositoryMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly GetIssuedInvoiceDetailHandler _handler;

    public GetIssuedInvoiceDetailHandlerTests()
    {
        _handler = new GetIssuedInvoiceDetailHandler(
            _repositoryMock.Object,
            _mapperMock.Object,
            Mock.Of<ILogger<GetIssuedInvoiceDetailHandler>>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError(string invoiceId)
    {
        // Arrange
        var request = new GetIssuedInvoiceDetailRequest
        {
            InvoiceId = invoiceId,
            WithDetails = true
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        response.Invoice.Should().BeNull();
        _repositoryMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WithDetailsTrue_CallsGetByIdWithSyncHistoryAsync()
    {
        // Arrange
        var request = new GetIssuedInvoiceDetailRequest
        {
            InvoiceId = "INV-TEST-001",
            WithDetails = true
        };
        var invoice = new IssuedInvoice
        {
            Id = "INV-TEST-001",
            SyncHistoryCount = 2
        };
        var mappedDto = new IssuedInvoiceDetailDto { Id = "INV-TEST-001" };

        _repositoryMock
            .Setup(r => r.GetByIdWithSyncHistoryAsync("INV-TEST-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _mapperMock
            .Setup(m => m.Map<IssuedInvoiceDetailDto>(invoice))
            .Returns(mappedDto);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Invoice.Should().Be(mappedDto);
        _repositoryMock.Verify(r => r.GetByIdWithSyncHistoryAsync("INV-TEST-001", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithDetailsFalse_CallsGetByIdAsync()
    {
        // Arrange
        var request = new GetIssuedInvoiceDetailRequest
        {
            InvoiceId = "INV-TEST-002",
            WithDetails = false
        };
        var invoice = new IssuedInvoice
        {
            Id = "INV-TEST-002",
            SyncHistoryCount = 0
        };
        var mappedDto = new IssuedInvoiceDetailDto { Id = "INV-TEST-002" };

        _repositoryMock
            .Setup(r => r.GetByIdAsync("INV-TEST-002", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _mapperMock
            .Setup(m => m.Map<IssuedInvoiceDetailDto>(invoice))
            .Returns(mappedDto);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Invoice.Should().Be(mappedDto);
        _repositoryMock.Verify(r => r.GetByIdAsync("INV-TEST-002", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.GetByIdWithSyncHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
