using Anela.Heblo.Application.Features.Bank.Infrastructure;
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
    private readonly Mock<IBankClientFactory> _mockFactory;
    private readonly Mock<IBankClient> _mockBankClient;
    private readonly Mock<IBankStatementImportService> _mockImportService;
    private readonly Mock<IBankStatementImportRepository> _mockRepository;
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
}
