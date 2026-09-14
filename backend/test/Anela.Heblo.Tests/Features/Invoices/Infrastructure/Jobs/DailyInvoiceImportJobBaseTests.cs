using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure.Jobs;

public sealed class DailyInvoiceImportJobBaseTests
{
    private const string TestJobName = "test-daily-invoice-import-job";
    private const string TestCurrency = "EUR";

    private readonly Mock<IInvoiceImportService> _importService = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    public DailyInvoiceImportJobBaseTests()
    {
        _statusChecker
            .Setup(c => c.IsJobEnabledAsync(TestJobName, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(true);
        _importService
            .Setup(s => s.ImportInvoicesAsync(It.IsAny<string>(), It.IsAny<IssuedInvoiceSourceQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportResultDto());
    }

    private TestDailyInvoiceImportJob CreateJob(ILoggerFactory? loggerFactory = null) => new(
        _importService.Object,
        loggerFactory ?? NullLoggerFactory.Instance,
        _statusChecker.Object,
        TestJobName,
        TestCurrency);

    [Fact]
    public async Task ExecuteAsync_ReturnsEarly_WhenJobIsDisabled()
    {
        _statusChecker
            .Setup(c => c.IsJobEnabledAsync(TestJobName, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(false);

        await CreateJob().ExecuteAsync(CancellationToken.None);

        _importService.Verify(
            s => s.ImportInvoicesAsync(It.IsAny<string>(), It.IsAny<IssuedInvoiceSourceQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_LogsWarning_AndCompletes_WhenSomeInvoicesFail()
    {
        _importService
            .Setup(s => s.ImportInvoicesAsync(It.IsAny<string>(), It.IsAny<IssuedInvoiceSourceQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportResultDto
            {
                Succeeded = new List<string> { "INV-001" },
                Failed = new List<string> { "INV-002" }
            });

        var loggerMock = new Mock<ILogger>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(loggerMock.Object);

        var act = () => CreateJob(loggerFactoryMock.Object).ExecuteAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Rethrows_WhenImportServiceThrows()
    {
        _importService
            .Setup(s => s.ImportInvoicesAsync(It.IsAny<string>(), It.IsAny<IssuedInvoiceSourceQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("import service unavailable"));

        var act = () => CreateJob().ExecuteAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("import service unavailable");
    }

    private sealed class TestDailyInvoiceImportJob : DailyInvoiceImportJobBase
    {
        public TestDailyInvoiceImportJob(
            IInvoiceImportService invoiceImportService,
            ILoggerFactory loggerFactory,
            IRecurringJobStatusChecker statusChecker,
            string jobName,
            string currency)
            : base(invoiceImportService, loggerFactory, statusChecker)
        {
            Metadata = new RecurringJobMetadata
            {
                JobName = jobName,
                DisplayName = "Test Daily Invoice Import Job",
                Description = "Test job for DailyInvoiceImportJobBase",
                CronExpression = "0 0 * * *",
                DefaultIsEnabled = true,
            };
            Currency = currency;
        }

        public override RecurringJobMetadata Metadata { get; }
        protected override string Currency { get; }
    }
}
