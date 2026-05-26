using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingInvoices;

public class MarketingInvoiceImportServiceTests
{
    private readonly Mock<IMarketingTransactionSource> _mockSource;
    private readonly Mock<IImportedMarketingTransactionRepository> _mockRepository;
    private readonly Mock<ILogger<MarketingInvoiceImportService>> _mockLogger;
    private readonly MarketingInvoiceImportService _service;

    public MarketingInvoiceImportServiceTests()
    {
        _mockSource = new Mock<IMarketingTransactionSource>();
        _mockRepository = new Mock<IImportedMarketingTransactionRepository>();
        _mockLogger = new Mock<ILogger<MarketingInvoiceImportService>>();

        _mockSource.Setup(x => x.Platform).Returns("TestPlatform");

        _service = new MarketingInvoiceImportService(
            _mockRepository.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task ImportAsync_NewTransactions_ArePersistedAndCounted()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-002", Amount = 200m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_DuplicateTransaction_IsSkipped()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", "TX-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_PerTransactionError_CountsAsFailed_DoesNotAbortRun()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-002", Amount = 200m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockRepository.Setup(x => x.AddAsync(It.Is<ImportedMarketingTransaction>(t => t.TransactionId == "TX-001"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockRepository.Setup(x => x.AddAsync(It.Is<ImportedMarketingTransaction>(t => t.TransactionId == "TX-002"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB write failed"));

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task ImportAsync_FinalSaveChangesThrows_ReportsAllStagedAsFailed_DoesNotThrow()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-002", Amount = 200m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // The single post-loop flush fails — none of the staged records are persisted
        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("flush failed"));

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(2, result.Failed);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_EmptyInput_DoesNotCallSaveChanges()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingTransaction>());

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_DuplicateTransactionIdWithinSameRun_StagesOnlyOnce()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        // Same TransactionId returned twice by the source in one run
        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-DUP", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-DUP", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        // Not present in the DB — ExistsAsync cannot see un-flushed staged entities
        _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_NewTransaction_PersistsCurrencyDescriptionAndRawData()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new()
            {
                TransactionId = "TX-EUR-001",
                Amount = 123.45m,
                TransactionDate = from,
                Description = "campaign X",
                Currency = "EUR",
                RawData = "{\"foo\":1}",
            },
        };

        _mockSource
            .Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository
            .Setup(x => x.ExistsAsync("TestPlatform", "TX-EUR-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        ImportedMarketingTransaction? captured = null;
        _mockRepository
            .Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<ImportedMarketingTransaction, CancellationToken>((entity, _) => captured = entity)
            .Returns(Task.CompletedTask);

        _mockRepository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
        Assert.NotNull(captured);
        Assert.Equal("EUR", captured!.Currency);
        Assert.Equal("campaign X", captured.Description);
        Assert.Equal("{\"foo\":1}", captured.RawData);
    }

    [Fact]
    public async Task ImportAsync_EmptyCurrency_Skips_CountsFailed_DoesNotCallExistsOrAdd()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new()
            {
                TransactionId = "TX-BAD-001",
                Amount = 100m,
                TransactionDate = from,
                Description = "missing currency",
                Currency = "",
            },
        };

        _mockSource
            .Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Failed);
        _mockRepository.Verify(
            x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepository.Verify(
            x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepository.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("TX-BAD-001") &&
                    v.ToString()!.Contains("TestPlatform")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ImportAsync_WhitespaceCurrency_TreatedAsEmpty_CountsFailed()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new()
            {
                TransactionId = "TX-WS-001",
                Amount = 50m,
                TransactionDate = from,
                Description = "whitespace currency",
                Currency = "   ",
            },
        };

        _mockSource
            .Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Failed);
        _mockRepository.Verify(
            x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepository.Verify(
            x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepository.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("TX-WS-001") &&
                    v.ToString()!.Contains("TestPlatform")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
