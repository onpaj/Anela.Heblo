using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class ImportBankStatementHandlerTests
{
    private readonly Mock<IBankClient> _mockBankClient;
    private readonly Mock<IBankStatementImportService> _mockImportService;
    private readonly Mock<IBankStatementImportRepository> _mockRepository;
    private readonly Mock<IMapper> _mockMapper;
    private readonly Mock<ILogger<ImportBankStatementHandler>> _mockLogger;
    private readonly BankAccountSettings _bankSettings;
    private readonly ImportBankStatementHandler _handler;

    public ImportBankStatementHandlerTests()
    {
        _mockBankClient = new Mock<IBankClient>();
        _mockImportService = new Mock<IBankStatementImportService>();
        _mockRepository = new Mock<IBankStatementImportRepository>();
        _mockMapper = new Mock<IMapper>();
        _mockLogger = new Mock<ILogger<ImportBankStatementHandler>>();

        _bankSettings = new BankAccountSettings
        {
            Accounts = new List<BankAccountConfiguration>
            {
                new BankAccountConfiguration
                {
                    Name = "CZK",
                    AccountNumber = "123456789",
                    FlexiBeeId = 1
                },
                new BankAccountConfiguration
                {
                    Name = "EUR",
                    AccountNumber = "987654321",
                    FlexiBeeId = 2
                }
            }
        };

        var optionsMock = new Mock<IOptions<BankAccountSettings>>();
        optionsMock.Setup(x => x.Value).Returns(_bankSettings);

        _handler = new ImportBankStatementHandler(
            _mockBankClient.Object,
            _mockImportService.Object,
            _mockRepository.Object,
            optionsMock.Object,
            _mockMapper.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act & Assert
        Assert.NotNull(_handler);
    }

    [Fact]
    public void Constructor_WithNullBankClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ImportBankStatementHandler(
            null!,
            _mockImportService.Object,
            _mockRepository.Object,
            Options.Create(_bankSettings),
            _mockMapper.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullImportService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ImportBankStatementHandler(
            _mockBankClient.Object,
            null!,
            _mockRepository.Object,
            Options.Create(_bankSettings),
            _mockMapper.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullRepository_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ImportBankStatementHandler(
            _mockBankClient.Object,
            _mockImportService.Object,
            null!,
            Options.Create(_bankSettings),
            _mockMapper.Object,
            _mockLogger.Object));
    }

    [Fact]
    public async Task Handle_WithUnknownAccount_ThrowsArgumentException()
    {
        // Arrange
        var request = new ImportBankStatementRequest("UNKNOWN", DateTime.Today);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.Handle(request, CancellationToken.None));

        Assert.Contains("Account name UNKNOWN not found", exception.Message);
    }

    [Fact]
    public async Task Handle_WithValidCZKAccount_CallsBankClientWithCorrectParameters()
    {
        // Arrange
        var request = new ImportBankStatementRequest("CZK", DateTime.Today);

        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", DateTime.Today))
            .ReturnsAsync(new List<BankStatementHeader>());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _mockBankClient.Verify(x => x.GetStatementsAsync("123456789", DateTime.Today), Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidEURAccount_CallsBankClientWithCorrectParameters()
    {
        // Arrange
        var request = new ImportBankStatementRequest("EUR", DateTime.Today);

        _mockBankClient.Setup(x => x.GetStatementsAsync("987654321", DateTime.Today))
            .ReturnsAsync(new List<BankStatementHeader>());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _mockBankClient.Verify(x => x.GetStatementsAsync("987654321", DateTime.Today), Times.Once);
    }
}