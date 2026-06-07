using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank.Infrastructure.Jobs;

public sealed class BankImportJobBaseTests
{
    private const string TestJobName = "test-bank-import-job";
    private const string TestAccountName = "TestAccount";
    private static readonly DateTime TestDateFrom = new(2026, 6, 1);
    private static readonly DateTime TestDateTo = new(2026, 6, 2);

    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    [Fact]
    public async Task ExecuteAsync_ReturnsEarly_WhenJobIsDisabled()
    {
        // Arrange
        _statusChecker
            .Setup(c => c.IsJobEnabledAsync(TestJobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var job = CreateJob();

        // Act
        await job.ExecuteAsync(CancellationToken.None);

        // Assert
        _mediator.Verify(
            m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        job.GetParametersCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_SendsImportRequest_WithParametersFromSubclass()
    {
        // Arrange
        _statusChecker
            .Setup(c => c.IsJobEnabledAsync(TestJobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        ImportBankStatementRequest? captured = null;
        _mediator
            .Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ImportBankStatementResponse>, CancellationToken>((req, _) =>
                captured = (ImportBankStatementRequest)req)
            .ReturnsAsync(new ImportBankStatementResponse());
        var job = CreateJob();

        // Act
        await job.ExecuteAsync(CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.AccountName.Should().Be(TestAccountName);
        captured.DateFrom.Should().Be(TestDateFrom);
        captured.DateTo.Should().Be(TestDateTo);
    }

    [Fact]
    public async Task ExecuteAsync_CallsGetParameters_ExactlyOnce()
    {
        // Arrange
        _statusChecker
            .Setup(c => c.IsJobEnabledAsync(TestJobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mediator
            .Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportBankStatementResponse());
        var job = CreateJob();

        // Act
        await job.ExecuteAsync(CancellationToken.None);

        // Assert
        job.GetParametersCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationToken_ToStatusCheckerAndMediator()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        _statusChecker
            .Setup(c => c.IsJobEnabledAsync(TestJobName, token))
            .ReturnsAsync(true);
        _mediator
            .Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), token))
            .ReturnsAsync(new ImportBankStatementResponse());
        var job = CreateJob();

        // Act
        await job.ExecuteAsync(token);

        // Assert — overload with the exact token must have been called (any other token would not match)
        _statusChecker.Verify(c => c.IsJobEnabledAsync(TestJobName, token), Times.Once);
        _mediator.Verify(
            m => m.Send(It.IsAny<ImportBankStatementRequest>(), token),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_LogsErrorAndRethrows_WhenMediatorThrows()
    {
        // Arrange
        var thrown = new InvalidOperationException("simulated handler failure");
        _statusChecker
            .Setup(c => c.IsJobEnabledAsync(TestJobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mediator
            .Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);

        var logger = new ListLogger();
        var loggerFactory = new SingleLoggerFactory(logger);
        var job = new TestBankImportJob(
            _mediator.Object,
            loggerFactory,
            _statusChecker.Object,
            new BankImportJobParameters(TestAccountName, TestDateFrom, TestDateTo),
            TestJobName);

        // Act
        Func<Task> act = () => job.ExecuteAsync(CancellationToken.None);

        // Assert
        var caught = await act.Should().ThrowAsync<InvalidOperationException>();
        caught.Which.Should().BeSameAs(thrown);
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains(TestJobName) &&
            e.Exception == thrown);
    }

    [Fact]
    public void Constructor_Throws_WhenMediatorIsNull()
    {
        var act = () => new TestBankImportJob(
            mediator: null!,
            loggerFactory: NullLoggerFactory.Instance,
            statusChecker: _statusChecker.Object,
            parameters: new BankImportJobParameters(TestAccountName, TestDateFrom, TestDateTo),
            jobName: TestJobName);
        act.Should().Throw<ArgumentNullException>().WithParameterName("mediator");
    }

    [Fact]
    public void Constructor_Throws_WhenLoggerFactoryIsNull()
    {
        var act = () => new TestBankImportJob(
            mediator: _mediator.Object,
            loggerFactory: null!,
            statusChecker: _statusChecker.Object,
            parameters: new BankImportJobParameters(TestAccountName, TestDateFrom, TestDateTo),
            jobName: TestJobName);
        act.Should().Throw<ArgumentNullException>().WithParameterName("loggerFactory");
    }

    [Fact]
    public void Constructor_Throws_WhenStatusCheckerIsNull()
    {
        var act = () => new TestBankImportJob(
            mediator: _mediator.Object,
            loggerFactory: NullLoggerFactory.Instance,
            statusChecker: null!,
            parameters: new BankImportJobParameters(TestAccountName, TestDateFrom, TestDateTo),
            jobName: TestJobName);
        act.Should().Throw<ArgumentNullException>().WithParameterName("statusChecker");
    }

    private TestBankImportJob CreateJob() => new(
        _mediator.Object,
        NullLoggerFactory.Instance,
        _statusChecker.Object,
        new BankImportJobParameters(TestAccountName, TestDateFrom, TestDateTo),
        TestJobName);

    private sealed class TestBankImportJob : BankImportJobBase
    {
        private readonly BankImportJobParameters _parameters;

        public TestBankImportJob(
            IMediator mediator,
            ILoggerFactory loggerFactory,
            IRecurringJobStatusChecker statusChecker,
            BankImportJobParameters parameters,
            string jobName)
            : base(mediator, loggerFactory, statusChecker)
        {
            _parameters = parameters;
            Metadata = new RecurringJobMetadata
            {
                JobName = jobName,
                DisplayName = "Test Bank Import Job",
                Description = "Test job for BankImportJobBase",
                CronExpression = "0 0 * * *",
                DefaultIsEnabled = true,
            };
        }

        public override RecurringJobMetadata Metadata { get; }

        public int GetParametersCallCount { get; private set; }

        internal override BankImportJobParameters GetParameters()
        {
            GetParametersCallCount++;
            return _parameters;
        }
    }

    private sealed class SingleLoggerFactory : ILoggerFactory
    {
        private readonly ILogger _logger;
        public SingleLoggerFactory(ILogger logger) => _logger = logger;
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => _logger;
        public void Dispose() { }
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullDisposable.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
