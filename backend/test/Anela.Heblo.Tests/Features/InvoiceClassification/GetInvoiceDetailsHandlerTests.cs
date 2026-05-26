using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetInvoiceDetails;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ApplicationContracts = Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class GetInvoiceDetailsHandlerTests
{
    private static IMapper BuildMapper()
    {
        var config = new MapperConfiguration(
            cfg => cfg.AddProfile<Anela.Heblo.Application.Features.InvoiceClassification.InvoiceClassificationMappingProfile>(),
            NullLoggerFactory.Instance);
        return config.CreateMapper();
    }

    [Fact]
    public async Task Handle_WhenInvoiceNotFound_ReturnsNullInvoiceAndFoundFalse()
    {
        // Arrange
        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock.Setup(c => c.GetInvoiceByIdAsync("missing"))
                  .ReturnsAsync((ReceivedInvoiceDto?)null);

        var handler = new GetInvoiceDetailsHandler(
            clientMock.Object,
            NullLogger<GetInvoiceDetailsHandler>.Instance,
            BuildMapper());

        // Act
        var response = await handler.Handle(
            new GetInvoiceDetailsRequest { InvoiceId = "missing" },
            CancellationToken.None);

        // Assert
        response.Invoice.Should().BeNull();
        response.Found.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenInvoiceFound_MapsToApplicationContract()
    {
        // Arrange
        var domainInvoice = new ReceivedInvoiceDto
        {
            InvoiceNumber = "FV-001",
            CompanyName = "Acme",
            Labels = Array.Empty<string>(),
        };

        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock.Setup(c => c.GetInvoiceByIdAsync("FV-001"))
                  .ReturnsAsync(domainInvoice);

        var handler = new GetInvoiceDetailsHandler(
            clientMock.Object,
            NullLogger<GetInvoiceDetailsHandler>.Instance,
            BuildMapper());

        // Act
        var response = await handler.Handle(
            new GetInvoiceDetailsRequest { InvoiceId = "FV-001" },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.Invoice.Should().NotBeNull();
        response.Invoice!.InvoiceNumber.Should().Be("FV-001");
        response.Invoice.CompanyName.Should().Be("Acme");
        // Confirm the runtime type is the Application contract, not the Domain class.
        response.Invoice.Should().BeOfType<ApplicationContracts.ReceivedInvoiceDto>();
    }
}
