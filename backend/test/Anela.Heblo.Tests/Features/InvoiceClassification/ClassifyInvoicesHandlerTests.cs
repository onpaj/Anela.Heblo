using Anela.Heblo.Application.Features.InvoiceClassification.Services;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class ClassifyInvoicesHandlerTests
{
    private readonly Mock<IReceivedInvoicesClient> _invoicesClientMock;
    private readonly Mock<IInvoiceClassificationService> _classificationServiceMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<ClassifyInvoicesHandler>> _loggerMock;
    private readonly ClassifyInvoicesHandler _handler;

    public ClassifyInvoicesHandlerTests()
    {
        _invoicesClientMock = new Mock<IReceivedInvoicesClient>();
        _classificationServiceMock = new Mock<IInvoiceClassificationService>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<ClassifyInvoicesHandler>>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("id", "test-user", "test@test.com", true));

        _handler = new ClassifyInvoicesHandler(
            _invoicesClientMock.Object,
            _classificationServiceMock.Object,
            _currentUserServiceMock.Object,
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
            .Setup(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>(), It.IsAny<string>()))
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
            .Setup(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>(), It.IsAny<string>()))
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
            .Setup(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>(), It.IsAny<string>()))
            .ReturnsAsync(new InvoiceClassificationResult { Result = ClassificationResult.Success });

        var request = new ClassifyInvoicesRequest { InvoiceIds = null };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.TotalInvoicesProcessed.Should().Be(1);
        response.SuccessfulClassifications.Should().Be(1);
        _invoicesClientMock.Verify(x => x.GetUnclassifiedInvoicesAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCurrentUserIsUnauthenticated_PassesSystemAsProcessedBy()
    {
        var unclassifiedInvoices = new List<ReceivedInvoice>
        {
            new() { InvoiceNumber = "UNCLASSIFIED-001", Labels = Array.Empty<string>() }
        };

        _invoicesClientMock
            .Setup(x => x.GetUnclassifiedInvoicesAsync())
            .ReturnsAsync(unclassifiedInvoices);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, null, null, false));

        _classificationServiceMock
            .Setup(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>(), It.IsAny<string>()))
            .ReturnsAsync(new InvoiceClassificationResult { Result = ClassificationResult.Success });

        var request = new ClassifyInvoicesRequest { InvoiceIds = null };

        await _handler.Handle(request, CancellationToken.None);

        _classificationServiceMock.Verify(
            x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>(), "system"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCurrentUserIsAuthenticated_PassesUserNameAsProcessedBy()
    {
        var unclassifiedInvoices = new List<ReceivedInvoice>
        {
            new() { InvoiceNumber = "UNCLASSIFIED-001", Labels = Array.Empty<string>() }
        };

        _invoicesClientMock
            .Setup(x => x.GetUnclassifiedInvoicesAsync())
            .ReturnsAsync(unclassifiedInvoices);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("id", "jane.doe", "jane.doe@test.com", true));

        _classificationServiceMock
            .Setup(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>(), It.IsAny<string>()))
            .ReturnsAsync(new InvoiceClassificationResult { Result = ClassificationResult.Success });

        var request = new ClassifyInvoicesRequest { InvoiceIds = null };

        await _handler.Handle(request, CancellationToken.None);

        _classificationServiceMock.Verify(
            x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>(), "jane.doe"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenErrorResultHasRuleName_IncludesRuleNameInErrorMessage()
    {
        var invoiceId = "INV-005";

        _invoicesClientMock
            .Setup(x => x.GetInvoiceByIdAsync(invoiceId))
            .ReturnsAsync(new ReceivedInvoice { InvoiceNumber = invoiceId, Labels = Array.Empty<string>() });

        _classificationServiceMock
            .Setup(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>(), It.IsAny<string>()))
            .ReturnsAsync(new InvoiceClassificationResult
            {
                Result = ClassificationResult.Error,
                RuleId = Guid.NewGuid(),
                RuleName = "My Rule",
                ErrorMessage = "boom"
            });

        var request = new ClassifyInvoicesRequest
        {
            InvoiceIds = new List<string> { invoiceId }
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Errors.Should().Be(1);
        response.ErrorMessages.Should().Contain($"Invoice {invoiceId} (Rule: My Rule): boom");
    }

    [Fact]
    public async Task Handle_WhenErrorResultHasNoRuleName_OmitsRuleSegmentFromErrorMessage()
    {
        var invoiceId = "INV-006";

        _invoicesClientMock
            .Setup(x => x.GetInvoiceByIdAsync(invoiceId))
            .ReturnsAsync(new ReceivedInvoice { InvoiceNumber = invoiceId, Labels = Array.Empty<string>() });

        _classificationServiceMock
            .Setup(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>(), It.IsAny<string>()))
            .ReturnsAsync(new InvoiceClassificationResult
            {
                Result = ClassificationResult.Error,
                RuleId = null,
                RuleName = null,
                ErrorMessage = "boom"
            });

        var request = new ClassifyInvoicesRequest
        {
            InvoiceIds = new List<string> { invoiceId }
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Errors.Should().Be(1);
        response.ErrorMessages.Should().Contain($"Invoice {invoiceId}: boom");
    }
}
