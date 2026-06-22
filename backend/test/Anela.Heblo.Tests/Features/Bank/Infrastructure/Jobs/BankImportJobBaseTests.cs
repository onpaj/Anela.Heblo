using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Bank;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank.Infrastructure.Jobs;

public sealed class BankImportJobBaseTests
{
    private const string TestJobName = "test-bank-import-job";
    private const string TestAccountName = "TestAccount";

    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();
    private readonly Mock<IBankImportStateRepository> _stateRepo = new();
    private readonly Mock<IBankStatementImportRepository> _statementRepo = new();
    private readonly BankImportWatermarkOptions _options = new() { MaxBackfillDays = 14, StaleWarningDays = 3 };

    public BankImportJobBaseTests()
    {
        _statusChecker.Setup(c => c.IsJobEnabledAsync(TestJobName, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(true);
        _mediator.Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportBankStatementResponse());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEarly_WhenJobIsDisabled()
    {
        _statusChecker.Setup(c => c.IsJobEnabledAsync(TestJobName, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(false);

        await CreateJob(targetEnd: new DateTime(2026, 6, 14)).ExecuteAsync(CancellationToken.None);

        _mediator.Verify(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _stateRepo.Verify(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_UsesWatermarkAsDateFrom_AndTargetAsDateTo()
    {
        var state = new BankImportState(TestAccountName);
        state.RecordSuccess(new DateTime(2026, 6, 10), DateTime.UtcNow, DateTime.UtcNow);
        _stateRepo.Setup(r => r.GetByAccountAsync(TestAccountName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        ImportBankStatementRequest? captured = null;
        _mediator.Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ImportBankStatementResponse>, CancellationToken>((req, _) => captured = (ImportBankStatementRequest)req)
            .ReturnsAsync(new ImportBankStatementResponse { SuccessCount = 1 });

        await CreateJob(targetEnd: new DateTime(2026, 6, 14)).ExecuteAsync(CancellationToken.None);

        captured!.AccountName.Should().Be(TestAccountName);
        captured.DateFrom.Should().Be(new DateTime(2026, 6, 10));
        captured.DateTo.Should().Be(new DateTime(2026, 6, 14));
    }

    [Fact]
    public async Task ExecuteAsync_Bootstraps_FromMaxStatementDate_WhenNoWatermark()
    {
        _stateRepo.Setup(r => r.GetByAccountAsync(TestAccountName, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankImportState?)null);
        _statementRepo.Setup(r => r.GetMaxStatementDateAsync(TestAccountName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DateTime(2026, 6, 12));

        ImportBankStatementRequest? captured = null;
        _mediator.Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ImportBankStatementResponse>, CancellationToken>((req, _) => captured = (ImportBankStatementRequest)req)
            .ReturnsAsync(new ImportBankStatementResponse());

        await CreateJob(targetEnd: new DateTime(2026, 6, 14)).ExecuteAsync(CancellationToken.None);

        captured!.DateFrom.Should().Be(new DateTime(2026, 6, 12));
    }

    [Fact]
    public async Task ExecuteAsync_ClampsDateFrom_ToMaxBackfillDays()
    {
        var state = new BankImportState(TestAccountName);
        state.RecordSuccess(new DateTime(2026, 5, 1), DateTime.UtcNow, DateTime.UtcNow); // ~44 days behind
        _stateRepo.Setup(r => r.GetByAccountAsync(TestAccountName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        ImportBankStatementRequest? captured = null;
        _mediator.Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ImportBankStatementResponse>, CancellationToken>((req, _) => captured = (ImportBankStatementRequest)req)
            .ReturnsAsync(new ImportBankStatementResponse());

        await CreateJob(targetEnd: new DateTime(2026, 6, 14)).ExecuteAsync(CancellationToken.None);

        captured!.DateFrom.Should().Be(new DateTime(2026, 6, 14).AddDays(-14)); // clamped
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotWriteState_OnZeroErrorRun()
    {
        // The handler owns BankImportState; the job must not write it (regression guard
        // against the double-write fixed in #3279).
        var state = new BankImportState(TestAccountName);
        state.RecordSuccess(new DateTime(2026, 6, 10), DateTime.UtcNow, DateTime.UtcNow);
        _stateRepo.Setup(r => r.GetByAccountAsync(TestAccountName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _mediator.Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportBankStatementResponse { SuccessCount = 0, ErrorCount = 0 }); // 0 docs = valid

        await CreateJob(targetEnd: new DateTime(2026, 6, 14)).ExecuteAsync(CancellationToken.None);

        _stateRepo.Verify(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotWriteState_OnErrorRun()
    {
        var state = new BankImportState(TestAccountName);
        state.RecordSuccess(new DateTime(2026, 6, 10), DateTime.UtcNow, DateTime.UtcNow);
        _stateRepo.Setup(r => r.GetByAccountAsync(TestAccountName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _mediator.Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportBankStatementResponse { SuccessCount = 1, ErrorCount = 2 });

        await CreateJob(targetEnd: new DateTime(2026, 6, 14)).ExecuteAsync(CancellationToken.None);

        _stateRepo.Verify(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotWriteState_AndRethrows_WhenHandlerThrows()
    {
        var state = new BankImportState(TestAccountName);
        state.RecordSuccess(new DateTime(2026, 6, 10), DateTime.UtcNow, DateTime.UtcNow);
        _stateRepo.Setup(r => r.GetByAccountAsync(TestAccountName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _mediator.Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bank client unavailable"));

        var act = () => CreateJob(targetEnd: new DateTime(2026, 6, 14)).ExecuteAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _stateRepo.Verify(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Constructor_Throws_WhenStateRepositoryIsNull()
    {
        var act = () => new TestBankImportJob(
            _mediator.Object, NullLoggerFactory.Instance, _statusChecker.Object,
            stateRepository: null!, _statementRepo.Object, Options.Create(_options),
            TestAccountName, new DateTime(2026, 6, 14), TestJobName);
        act.Should().Throw<ArgumentNullException>().WithParameterName("stateRepository");
    }

    private TestBankImportJob CreateJob(DateTime targetEnd) => new(
        _mediator.Object, NullLoggerFactory.Instance, _statusChecker.Object,
        _stateRepo.Object, _statementRepo.Object, Options.Create(_options),
        TestAccountName, targetEnd, TestJobName);

    private sealed class TestBankImportJob : BankImportJobBase
    {
        private readonly string _accountName;
        private readonly DateTime _targetEnd;

        public TestBankImportJob(
            IMediator mediator,
            ILoggerFactory loggerFactory,
            IRecurringJobStatusChecker statusChecker,
            IBankImportStateRepository stateRepository,
            IBankStatementImportRepository statementRepository,
            IOptions<BankImportWatermarkOptions> options,
            string accountName,
            DateTime targetEnd,
            string jobName)
            : base(mediator, loggerFactory, statusChecker, stateRepository, statementRepository, options)
        {
            _accountName = accountName;
            _targetEnd = targetEnd;
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
        protected override string AccountName => _accountName;
        protected override DateTime GetTargetEndDate(DateTime today) => _targetEnd;
    }
}
