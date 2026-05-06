using System.Collections.Concurrent;
using System.Diagnostics;
using Anela.Heblo.Application.Features.InvoiceClassification.Services;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class ClassifyInvoicesHandlerTests
{
    private static InvoiceClassificationResult SuccessResult() => new()
    {
        Result = ClassificationResult.Success
    };

    private static ReceivedInvoiceDto Invoice(string id) => new()
    {
        InvoiceNumber = id,
        CompanyName = $"Company-{id}",
        TotalAmount = 100m,
        Description = "test"
    };

    private static ClassifyInvoicesHandler BuildHandler(
        IReceivedInvoicesClient invoicesClient,
        IInvoiceClassificationService? classificationService = null,
        IClassificationRuleRepository? ruleRepository = null)
    {
        classificationService ??= Mock.Of<IInvoiceClassificationService>(s =>
            s.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoiceDto>()) ==
            Task.FromResult(SuccessResult()));

        ruleRepository ??= Mock.Of<IClassificationRuleRepository>();

        return new ClassifyInvoicesHandler(
            invoicesClient,
            classificationService,
            ruleRepository,
            NullLogger<ClassifyInvoicesHandler>.Instance);
    }

    [Fact]
    public async Task Handle_FetchesInParallel_WhenMultipleIds()
    {
        // Arrange: 10 ids, each fake fetch sleeps 200 ms.
        // Sequential would be ~2000 ms; with concurrency >= 5 we expect well under 800 ms.
        var ids = Enumerable.Range(1, 10).Select(i => $"INV-{i}").ToList();

        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock
            .Setup(c => c.GetInvoiceByIdAsync(It.IsAny<string>()))
            .Returns<string>(async id =>
            {
                await Task.Delay(200);
                return Invoice(id);
            });

        var handler = BuildHandler(clientMock.Object);
        var request = new ClassifyInvoicesRequest { InvoiceIds = ids };

        // Act
        var sw = Stopwatch.StartNew();
        var response = await handler.Handle(request, CancellationToken.None);
        sw.Stop();

        // Assert
        response.TotalInvoicesProcessed.Should().Be(10);
        response.Errors.Should().Be(0);
        sw.ElapsedMilliseconds.Should().BeLessThan(800,
            "10 fetches at 200ms each should fan out under the throttle, not run sequentially");
    }
}
