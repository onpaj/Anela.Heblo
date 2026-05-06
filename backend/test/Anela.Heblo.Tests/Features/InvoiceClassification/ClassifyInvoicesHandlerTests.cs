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

    private sealed class TrackingFakeClient : IReceivedInvoicesClient
    {
        private int _inFlight;
        public int MaxObservedInFlight;
        private readonly TimeSpan _delay;

        public TrackingFakeClient(TimeSpan delay)
        {
            _delay = delay;
        }

        public Task<List<ReceivedInvoiceDto>> GetUnclassifiedInvoicesAsync() =>
            Task.FromResult(new List<ReceivedInvoiceDto>());

        public async Task<ReceivedInvoiceDto?> GetInvoiceByIdAsync(string invoiceId)
        {
            var current = Interlocked.Increment(ref _inFlight);
            // Update peak using atomic CAS loop.
            int observed;
            do
            {
                observed = MaxObservedInFlight;
                if (current <= observed) break;
            }
            while (Interlocked.CompareExchange(ref MaxObservedInFlight, current, observed) != observed);

            try
            {
                await Task.Delay(_delay);
                return new ReceivedInvoiceDto
                {
                    InvoiceNumber = invoiceId,
                    CompanyName = $"Company-{invoiceId}",
                    TotalAmount = 100m,
                    Description = "test"
                };
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    [Fact]
    public async Task Handle_RespectsConcurrencyLimit()
    {
        // Arrange: 30 ids, 100 ms per fake fetch — guarantees the throttle saturates.
        const int MaxFetchConcurrency = 8; // mirrors the handler's private constant
        var ids = Enumerable.Range(1, 30).Select(i => $"INV-{i}").ToList();
        var fakeClient = new TrackingFakeClient(TimeSpan.FromMilliseconds(100));

        var handler = BuildHandler(fakeClient);
        var request = new ClassifyInvoicesRequest { InvoiceIds = ids };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.TotalInvoicesProcessed.Should().Be(30);
        fakeClient.MaxObservedInFlight.Should().BeLessThanOrEqualTo(MaxFetchConcurrency,
            "the SemaphoreSlim throttle must cap concurrent Flexi calls");
    }

    [Fact]
    public async Task Handle_AppendsNotFoundError_AndContinues()
    {
        // Arrange: B is missing; A and C succeed.
        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock.Setup(c => c.GetInvoiceByIdAsync("A")).ReturnsAsync(Invoice("A"));
        clientMock.Setup(c => c.GetInvoiceByIdAsync("B")).ReturnsAsync((ReceivedInvoiceDto?)null);
        clientMock.Setup(c => c.GetInvoiceByIdAsync("C")).ReturnsAsync(Invoice("C"));

        var classifiedIds = new ConcurrentBag<string>();
        var classificationMock = new Mock<IInvoiceClassificationService>();
        classificationMock
            .Setup(s => s.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoiceDto>()))
            .Returns<ReceivedInvoiceDto>(inv =>
            {
                classifiedIds.Add(inv.InvoiceNumber);
                return Task.FromResult(SuccessResult());
            });

        var handler = BuildHandler(clientMock.Object, classificationMock.Object);
        var request = new ClassifyInvoicesRequest { InvoiceIds = new List<string> { "A", "B", "C" } };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Errors.Should().Be(1);
        response.ErrorMessages.Should().ContainSingle(m => m == "Invoice B not found");
        response.SuccessfulClassifications.Should().Be(2);
        classifiedIds.Should().BeEquivalentTo(new[] { "A", "C" });
    }

    [Fact]
    public async Task Handle_FetchExceptionIsolatedToOneId()
    {
        // Arrange: B throws; A and C succeed.
        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock.Setup(c => c.GetInvoiceByIdAsync("A")).ReturnsAsync(Invoice("A"));
        clientMock.Setup(c => c.GetInvoiceByIdAsync("B")).ThrowsAsync(new InvalidOperationException("flexi-network-down"));
        clientMock.Setup(c => c.GetInvoiceByIdAsync("C")).ReturnsAsync(Invoice("C"));

        var classifiedIds = new ConcurrentBag<string>();
        var classificationMock = new Mock<IInvoiceClassificationService>();
        classificationMock
            .Setup(s => s.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoiceDto>()))
            .Returns<ReceivedInvoiceDto>(inv =>
            {
                classifiedIds.Add(inv.InvoiceNumber);
                return Task.FromResult(SuccessResult());
            });

        var handler = BuildHandler(clientMock.Object, classificationMock.Object);
        var request = new ClassifyInvoicesRequest { InvoiceIds = new List<string> { "A", "B", "C" } };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Errors.Should().Be(1);
        response.ErrorMessages.Should().ContainSingle()
            .Which.Should().Be("Invoice B: fetch failed: flexi-network-down");
        response.SuccessfulClassifications.Should().Be(2);
        classifiedIds.Should().BeEquivalentTo(new[] { "A", "C" });
    }

    [Fact]
    public async Task Handle_PreservesInputOrderOfErrors()
    {
        // Arrange: input order [A, B, C, D, E].
        // A: throws (slowest)
        // B: not found
        // C: throws
        // D: not found
        // E: succeeds (fastest)
        // If the handler appended errors in completion order, E would not appear in errors and the rest
        // would be reversed; we assert input-order to lock in FR-5.
        var clientMock = new Mock<IReceivedInvoicesClient>();

        clientMock.Setup(c => c.GetInvoiceByIdAsync("A"))
            .Returns<string>(async _ =>
            {
                await Task.Delay(250);
                throw new InvalidOperationException("err-A");
            });
        clientMock.Setup(c => c.GetInvoiceByIdAsync("B"))
            .Returns<string>(async _ =>
            {
                await Task.Delay(200);
                return null;
            });
        clientMock.Setup(c => c.GetInvoiceByIdAsync("C"))
            .Returns<string>(async _ =>
            {
                await Task.Delay(150);
                throw new InvalidOperationException("err-C");
            });
        clientMock.Setup(c => c.GetInvoiceByIdAsync("D"))
            .Returns<string>(async _ =>
            {
                await Task.Delay(100);
                return null;
            });
        clientMock.Setup(c => c.GetInvoiceByIdAsync("E"))
            .Returns<string>(async _ =>
            {
                await Task.Delay(50);
                return Invoice("E");
            });

        var handler = BuildHandler(clientMock.Object);
        var request = new ClassifyInvoicesRequest { InvoiceIds = new List<string> { "A", "B", "C", "D", "E" } };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert: errors ordered A, B, C, D — strict input order.
        response.Errors.Should().Be(4);
        response.ErrorMessages.Should().Equal(
            "Invoice A: fetch failed: err-A",
            "Invoice B not found",
            "Invoice C: fetch failed: err-C",
            "Invoice D not found");
        response.SuccessfulClassifications.Should().Be(1);
    }

    [Fact]
    public async Task Handle_BatchModeUnchanged_NullInvoiceIds()
    {
        // Arrange: null InvoiceIds means batch mode.
        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock.Setup(c => c.GetUnclassifiedInvoicesAsync())
            .ReturnsAsync(new List<ReceivedInvoiceDto> { Invoice("X"), Invoice("Y") });

        var handler = BuildHandler(clientMock.Object);
        var request = new ClassifyInvoicesRequest { InvoiceIds = null };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.TotalInvoicesProcessed.Should().Be(2);
        clientMock.Verify(c => c.GetUnclassifiedInvoicesAsync(), Times.Once);
        clientMock.Verify(c => c.GetInvoiceByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_BatchModeUnchanged_EmptyInvoiceIds()
    {
        // Arrange: empty list also means batch mode.
        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock.Setup(c => c.GetUnclassifiedInvoicesAsync())
            .ReturnsAsync(new List<ReceivedInvoiceDto>());

        var handler = BuildHandler(clientMock.Object);
        var request = new ClassifyInvoicesRequest { InvoiceIds = new List<string>() };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.TotalInvoicesProcessed.Should().Be(0);
        clientMock.Verify(c => c.GetUnclassifiedInvoicesAsync(), Times.Once);
        clientMock.Verify(c => c.GetInvoiceByIdAsync(It.IsAny<string>()), Times.Never);
    }
}
