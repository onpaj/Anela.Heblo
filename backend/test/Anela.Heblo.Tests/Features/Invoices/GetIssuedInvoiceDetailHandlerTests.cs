using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
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
}
