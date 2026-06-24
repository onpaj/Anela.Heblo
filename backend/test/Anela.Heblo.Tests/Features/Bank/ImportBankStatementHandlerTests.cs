using Anela.Heblo.Application.Features.Bank.Infrastructure;
using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using AutoMapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class ImportBankStatementHandlerTests
{
    private readonly Mock<IBankClientFactory> _mockFactory;
    private readonly Mock<IBankClient> _mockBankClient;
    private readonly Mock<IBankStatementImportService> _mockImportService;
    private readonly Mock<IBankStatementImportRepository> _mockRepository;
    private readonly Mock<IBankImportStateRepository> _mockStateRepository;
    private readonly Mock<IMapper> _mockMapper;
    private readonly Mock<ILogger<ImportBankStatementHandler>> _mockLogger;
    private readonly BankAccountSettings _bankSettings;
    private readonly ImportBankStatementHandler _handler;

    public ImportBankStatementHandlerTests()
    {
        _mockFactory = new Mock<IBankClientFactory>();
        _mockBankClient = new Mock<IBankClient>();
        _mockImportService = new Mock<IBankStatementImportService>();
        _mockRepository = new Mock<IBankStatementImportRepository>();
        _mockStateRepository = new Mock<IBankImportStateRepository>();
        _mockMapper = new Mock<IMapper>();
        _mockLogger = new Mock<ILogger<ImportBankStatementHandler>>();

        _bankSettings = new BankAccountSettings
        {
            Accounts = new List<BankAccountConfiguration>
            {
                new BankAccountConfiguration
                {
                    Name = "ComgateCZK",
                    Provider = BankClientProvider.Comgate,
                    AccountNumber = "123456789",
                    FlexiBeeId = 1,
                    Currency = CurrencyCode.CZK
                },
                new BankAccountConfiguration
                {
                    Name = "ComgateEUR",
                    Provider = BankClientProvider.Comgate,
                    AccountNumber = "987654321",
                    FlexiBeeId = 2,
                    Currency = CurrencyCode.EUR
                }
            }
        };

        _mockFactory.Setup(x => x.GetClient(It.IsAny<BankAccountConfiguration>()))
            .Returns(_mockBankClient.Object);

        _handler = new ImportBankStatementHandler(
            _mockFactory.Object,
            _mockImportService.Object,
            _mockRepository.Object,
            Options.Create(_bankSettings),
            _mockStateRepository.Object,
            Options.Create(new BankImportWatermarkOptions()),
            _mockMapper.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithNullFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ImportBankStatementHandler(
            null!,
            _mockImportService.Object,
            _mockRepository.Object,
            Options.Create(_bankSettings),
            _mockStateRepository.Object,
            Options.Create(new BankImportWatermarkOptions()),
            _mockMapper.Object,
            _mockLogger.Object));
    }

    [Fact]
    public async Task Handle_WithUnknownAccount_ThrowsArgumentException()
    {
        var request = new ImportBankStatementRequest("UNKNOWN", DateTime.Today, DateTime.Today);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.Handle(request, CancellationToken.None));

        Assert.Contains("Account name UNKNOWN not found", exception.Message);
    }

    [Fact]
    public async Task Handle_WithValidAccount_ResolvesClientViaFactory()
    {
        var dateFrom = DateTime.Today.AddDays(-1);
        var dateTo = DateTime.Today;
        var request = new ImportBankStatementRequest("ComgateCZK", dateFrom, dateTo);

        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", dateFrom, dateTo))
            .ReturnsAsync(new List<BankStatementHeader>());

        await _handler.Handle(request, CancellationToken.None);

        _mockFactory.Verify(x => x.GetClient(It.Is<BankAccountConfiguration>(c => c.Name == "ComgateCZK")), Times.Once);
        _mockBankClient.Verify(x => x.GetStatementsAsync("123456789", dateFrom, dateTo), Times.Once);
    }

    [Fact]
    public async Task Handle_SkipsAlreadySucceededStatements_NoFlexiBeePush()
    {
        var from = new DateTime(2026, 6, 10);
        var to = new DateTime(2026, 6, 10);
        var request = new ImportBankStatementRequest("ComgateCZK", from, to);

        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", from, to))
            .ReturnsAsync(new List<BankStatementHeader>
            {
                new BankStatementHeader { StatementId = "DONE", Date = from },
            });
        _mockRepository.Setup(r => r.GetExistingResultsByTransferIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["DONE"] = ImportStatus.Success });

        var response = await _handler.Handle(request, CancellationToken.None);

        response.SkippedCount.Should().Be(1);
        response.SuccessCount.Should().Be(0);
        response.ErrorCount.Should().Be(0);
        _mockBankClient.Verify(x => x.GetStatementAsync(It.IsAny<string>()), Times.Never);
        _mockImportService.Verify(
            x => x.ImportStatementAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        _mockRepository.Verify(r => r.AddAsync(It.IsAny<BankStatementImport>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RetriesPreviouslyFailedStatement_ViaUpdateNotAdd()
    {
        var from = new DateTime(2026, 6, 10);
        var to = new DateTime(2026, 6, 10);
        var request = new ImportBankStatementRequest("ComgateCZK", from, to);
        var existingRow = new BankStatementImport("RETRY", from);

        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", from, to))
            .ReturnsAsync(new List<BankStatementHeader>
            {
                new BankStatementHeader { StatementId = "RETRY", Date = from },
            });
        _mockRepository.Setup(r => r.GetExistingResultsByTransferIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["RETRY"] = $"{ImportStatus.ProcessingError}: old" });
        _mockBankClient.Setup(x => x.GetStatementAsync("RETRY"))
            .ReturnsAsync(new BankStatementData { Data = "abo", ItemCount = 3 });
        _mockImportService.Setup(x => x.ImportStatementAsync(1, "abo"))
            .ReturnsAsync(Result<bool>.Success(true));
        _mockRepository.Setup(r => r.GetByTransferIdAsync("RETRY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRow);
        _mockRepository.Setup(r => r.UpdateAsync(It.IsAny<BankStatementImport>()))
            .ReturnsAsync((BankStatementImport b) => b);
        _mockMapper.Setup(m => m.Map<Anela.Heblo.Application.Features.Bank.Contracts.BankStatementImportDto>(
                It.IsAny<BankStatementImport>()))
            .Returns(new Anela.Heblo.Application.Features.Bank.Contracts.BankStatementImportDto
            {
                TransferId = "RETRY",
                ImportResult = ImportStatus.Success,
            });

        var response = await _handler.Handle(request, CancellationToken.None);

        response.SuccessCount.Should().Be(1);
        response.ErrorCount.Should().Be(0);
        _mockRepository.Verify(r => r.UpdateAsync(It.IsAny<BankStatementImport>()), Times.Once);
        _mockRepository.Verify(r => r.AddAsync(It.IsAny<BankStatementImport>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RecordsSuccessWatermark_WhenNoErrors()
    {
        var from = new DateTime(2026, 6, 10);
        var to = new DateTime(2026, 6, 12);
        var request = new ImportBankStatementRequest("ComgateCZK", from, to);

        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", from, to))
            .ReturnsAsync(new List<BankStatementHeader>());

        BankImportState? captured = null;
        _mockStateRepository
            .Setup(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()))
            .Callback<BankImportState, CancellationToken>((s, _) => captured = s);

        await _handler.Handle(request, CancellationToken.None);

        _mockStateRepository.Verify(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()), Times.Once);
        captured!.LastRunStatus.Should().Be(BankImportState.StatusOk);
        captured.LastValidImportDate.Should().Be(to.Date);
        captured.ConsecutiveFailureCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_RecordsFailureWatermark_WhenStatementFails()
    {
        var from = new DateTime(2026, 6, 10);
        var to = new DateTime(2026, 6, 10);
        var request = new ImportBankStatementRequest("ComgateCZK", from, to);

        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", from, to))
            .ReturnsAsync(new List<BankStatementHeader>
            {
                new BankStatementHeader { StatementId = "FAIL", Date = from },
            });
        _mockRepository.Setup(r => r.GetExistingResultsByTransferIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        _mockBankClient.Setup(x => x.GetStatementAsync("FAIL"))
            .ThrowsAsync(new Exception("bank unavailable"));
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<BankStatementImport>()))
            .ReturnsAsync((BankStatementImport b) => b);
        _mockMapper.Setup(m => m.Map<Anela.Heblo.Application.Features.Bank.Contracts.BankStatementImportDto>(
                It.IsAny<BankStatementImport>()))
            .Returns(new Anela.Heblo.Application.Features.Bank.Contracts.BankStatementImportDto
            {
                TransferId = "FAIL",
                ImportResult = $"{ImportStatus.ProcessingError}: bank unavailable",
            });

        BankImportState? captured = null;
        _mockStateRepository
            .Setup(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()))
            .Callback<BankImportState, CancellationToken>((s, _) => captured = s);

        await _handler.Handle(request, CancellationToken.None);

        _mockStateRepository.Verify(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()), Times.Once);
        captured!.LastRunStatus.Should().Be(BankImportState.StatusError);
        captured.LastValidImportDate.Should().BeNull();
        captured.ConsecutiveFailureCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_UsesExistingWatermarkState_WhenAccountKnown()
    {
        var from = new DateTime(2026, 6, 10);
        var to = new DateTime(2026, 6, 10);
        var request = new ImportBankStatementRequest("ComgateCZK", from, to);
        var existingState = new BankImportState("ComgateCZK");
        existingState.RecordSuccess(new DateTime(2026, 6, 9), DateTime.UtcNow, DateTime.UtcNow);

        _mockStateRepository
            .Setup(r => r.GetByAccountAsync("ComgateCZK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingState);
        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", from, to))
            .ReturnsAsync(new List<BankStatementHeader>());

        BankImportState? captured = null;
        _mockStateRepository
            .Setup(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()))
            .Callback<BankImportState, CancellationToken>((s, _) => captured = s);

        await _handler.Handle(request, CancellationToken.None);

        captured.Should().BeSameAs(existingState);
        captured!.LastValidImportDate.Should().Be(to.Date);
    }

    [Fact]
    public async Task Handle_RecordsFailure_AndRethrows_WhenImportThrows()
    {
        var from = new DateTime(2026, 6, 10);
        var to = new DateTime(2026, 6, 12);
        var request = new ImportBankStatementRequest("ComgateCZK", from, to);

        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", from, to))
            .ThrowsAsync(new InvalidOperationException("bank client unavailable"));

        BankImportState? captured = null;
        _mockStateRepository
            .Setup(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()))
            .Callback<BankImportState, CancellationToken>((s, _) => captured = s);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.Handle(request, CancellationToken.None));

        _mockStateRepository.Verify(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()), Times.Once);
        captured!.LastRunStatus.Should().Be(BankImportState.StatusError);
        captured.LastValidImportDate.Should().BeNull();
        captured.LastErrorMessage.Should().Be("bank client unavailable");
        captured.ConsecutiveFailureCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_CollapsesDuplicateStatementIdsInResponse_ProcessesOnce()
    {
        var from = new DateTime(2026, 6, 10);
        var to = new DateTime(2026, 6, 10);
        var request = new ImportBankStatementRequest("ComgateCZK", from, to);

        // Bank returns the same StatementId twice in a single response.
        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", from, to))
            .ReturnsAsync(new List<BankStatementHeader>
            {
                new BankStatementHeader { StatementId = "DUP", Date = from },
                new BankStatementHeader { StatementId = "DUP", Date = from },
            });
        _mockRepository.Setup(r => r.GetExistingResultsByTransferIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        _mockBankClient.Setup(x => x.GetStatementAsync("DUP"))
            .ReturnsAsync(new BankStatementData { Data = "abo", ItemCount = 1 });
        _mockImportService.Setup(x => x.ImportStatementAsync(1, "abo"))
            .ReturnsAsync(Result<bool>.Success(true));
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<BankStatementImport>()))
            .ReturnsAsync((BankStatementImport b) => b);
        _mockMapper.Setup(m => m.Map<Anela.Heblo.Application.Features.Bank.Contracts.BankStatementImportDto>(
                It.IsAny<BankStatementImport>()))
            .Returns(new Anela.Heblo.Application.Features.Bank.Contracts.BankStatementImportDto
            {
                TransferId = "DUP",
                ImportResult = ImportStatus.Success,
            });

        var response = await _handler.Handle(request, CancellationToken.None);

        response.SuccessCount.Should().Be(1);
        _mockBankClient.Verify(x => x.GetStatementAsync("DUP"), Times.Once);
        _mockRepository.Verify(r => r.AddAsync(It.IsAny<BankStatementImport>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DoesNotDoubleInsert_WhenPersistenceFails()
    {
        var from = new DateTime(2026, 6, 10);
        var to = new DateTime(2026, 6, 10);
        var request = new ImportBankStatementRequest("ComgateCZK", from, to);

        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", from, to))
            .ReturnsAsync(new List<BankStatementHeader>
            {
                new BankStatementHeader { StatementId = "X", Date = from },
            });
        _mockRepository.Setup(r => r.GetExistingResultsByTransferIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        _mockBankClient.Setup(x => x.GetStatementAsync("X"))
            .ReturnsAsync(new BankStatementData { Data = "abo", ItemCount = 2 });
        _mockImportService.Setup(x => x.ImportStatementAsync(1, "abo"))
            .ReturnsAsync(Result<bool>.Success(true));
        // The INSERT fails (e.g. duplicate-key violation surfaced by the DB).
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<BankStatementImport>()))
            .ThrowsAsync(new DbUpdateException("duplicate key", (Exception?)null));

        BankImportState? captured = null;
        _mockStateRepository
            .Setup(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()))
            .Callback<BankImportState, CancellationToken>((s, _) => captured = s);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => _handler.Handle(request, CancellationToken.None));

        // Persistence is attempted exactly once - the error path must not re-insert.
        _mockRepository.Verify(r => r.AddAsync(It.IsAny<BankStatementImport>()), Times.Once);
        // The failure is still recorded on the watermark state.
        _mockStateRepository.Verify(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()), Times.Once);
        captured!.LastRunStatus.Should().Be(BankImportState.StatusError);
    }
}
