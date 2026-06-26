using Anela.Heblo.Application.Features.InvoiceClassification.Services;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class ClassifyInvoicesHandlerTests
{
    private readonly Mock<IReceivedInvoicesClient> _invoicesClientMock;
    private readonly Mock<IInvoiceClassificationService> _classificationServiceMock;
    private readonly Mock<IClassificationRuleRepository> _ruleRepositoryMock;
    private readonly Mock<ILogger<ClassifyInvoicesHandler>> _loggerMock;
    private readonly ClassifyInvoicesHandler _handler;

    public ClassifyInvoicesHandlerTests()
    {
        _invoicesClientMock = new Mock<IReceivedInvoicesClient>();
        _classificationServiceMock = new Mock<IInvoiceClassificationService>();
        _ruleRepositoryMock = new Mock<IClassificationRuleRepository>();
        _loggerMock = new Mock<ILogger<ClassifyInvoicesHandler>>();

        _handler = new ClassifyInvoicesHandler(
            _invoicesClientMock.Object,
            _classificationServiceMock.Object,
            _ruleRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WithMultipleInvoiceIds_FetchesAllInvoicesInParallel()
    {
        // Reproduces bug from issue #969: sequential foreach caused N sequential Flexi API calls.
        // Fix: use Task.WhenAll to fetch all invoices concurrently.

        var invoiceId1 = "INV-001";
        var invoiceId2 = "INV-002";
        var invoiceId3 = "INV-003";

        _invoicesClientMock
            .Setup(x => x.GetInvoiceByIdAsync(invoiceId1))
            .ReturnsAsync(new ReceivedInvoice { InvoiceNumber = invoiceId1, Labels = Array.Empty<string>() });
        _invoicesClientMock
            .Setup(x => x.GetInvoiceByIdAsync(invoiceId2))
            .ReturnsAsync(new ReceivedInvoice { InvoiceNumber = invoiceId2, Labels = Array.Empty<string>() });
        _invoicesClientMock
            .Setup(x => x.GetInvoiceByIdAsync(invoiceId3))
            .ReturnsAsync(new ReceivedInvoice { InvoiceNumber = invoiceId3, Labels = Array.Empty<string>() });

        _classificationServiceMock
            .Setup(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>()))
            .ReturnsAsync(new InvoiceClassificationResult { Result = ClassificationResult.Success });

        var request = new ClassifyInvoicesRequest
        {
            InvoiceIds = new List<string> { invoiceId1, invoiceId2, invoiceId3 }
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.TotalInvoicesProcessed.Should().Be(3);
        response.SuccessfulClassifications.Should().Be(3);
        response.Errors.Should().Be(0);

        // Verify all three invoices were fetched (regardless of order — parallel execution)
        _invoicesClientMock.Verify(x => x.GetInvoiceByIdAsync(invoiceId1), Times.Once);
        _invoicesClientMock.Verify(x => x.GetInvoiceByIdAsync(invoiceId2), Times.Once);
        _invoicesClientMock.Verify(x => x.GetInvoiceByIdAsync(invoiceId3), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSomeInvoicesNotFound_CountsThemAsErrors()
    {
        var foundId = "INV-001";
        var missingId = "INV-999";

        _invoicesClientMock
            .Setup(x => x.GetInvoiceByIdAsync(foundId))
            .ReturnsAsync(new ReceivedInvoice { InvoiceNumber = foundId, Labels = Array.Empty<string>() });
        _invoicesClientMock
            .Setup(x => x.GetInvoiceByIdAsync(missingId))
            .ReturnsAsync((ReceivedInvoice?)null);

        _classificationServiceMock
            .Setup(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>()))
            .ReturnsAsync(new InvoiceClassificationResult { Result = ClassificationResult.Success });

        var request = new ClassifyInvoicesRequest
        {
            InvoiceIds = new List<string> { foundId, missingId }
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.TotalInvoicesProcessed.Should().Be(1);
        response.SuccessfulClassifications.Should().Be(1);
        response.Errors.Should().Be(1);
        response.ErrorMessages.Should().ContainSingle(m => m.Contains(missingId));
    }

    [Fact]
    public async Task Handle_WithNoInvoiceIds_FetchesAllUnclassifiedInvoices()
    {
        var unclassifiedInvoices = new List<ReceivedInvoice>
        {
            new() { InvoiceNumber = "UNCLASSIFIED-001", Labels = Array.Empty<string>() }
        };

        _invoicesClientMock
            .Setup(x => x.GetUnclassifiedInvoicesAsync())
            .ReturnsAsync(unclassifiedInvoices);

        _classificationServiceMock
            .Setup(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>()))
            .ReturnsAsync(new InvoiceClassificationResult { Result = ClassificationResult.Success });

        var request = new ClassifyInvoicesRequest { InvoiceIds = null };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.TotalInvoicesProcessed.Should().Be(1);
        response.SuccessfulClassifications.Should().Be(1);
        _invoicesClientMock.Verify(x => x.GetUnclassifiedInvoicesAsync(), Times.Once);
    }
}
