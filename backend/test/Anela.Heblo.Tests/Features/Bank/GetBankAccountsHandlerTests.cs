using Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class GetBankAccountsHandlerTests
{
    private readonly Mock<ILogger<GetBankAccountsHandler>> _mockLogger;

    public GetBankAccountsHandlerTests()
    {
        _mockLogger = new Mock<ILogger<GetBankAccountsHandler>>();
    }

    private GetBankAccountsHandler CreateHandler(BankAccountSettings settings)
    {
        return new GetBankAccountsHandler(Options.Create(settings), _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WithNullAccountsList_ReturnsEmptyResponse()
    {
        var settings = new BankAccountSettings { Accounts = null! };
        var handler = CreateHandler(settings);

        var response = await handler.Handle(new GetBankAccountsRequest(), CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotNull(response.Accounts);
        Assert.Empty(response.Accounts);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task Handle_WithEmptyAccountsList_ReturnsEmptyResponse()
    {
        var settings = new BankAccountSettings { Accounts = new List<BankAccountConfiguration>() };
        var handler = CreateHandler(settings);

        var response = await handler.Handle(new GetBankAccountsRequest(), CancellationToken.None);

        Assert.NotNull(response.Accounts);
        Assert.Empty(response.Accounts);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task Handle_WithConfiguredAccounts_MapsEachAccountToDto()
    {
        var settings = new BankAccountSettings
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
        var handler = CreateHandler(settings);

        var response = await handler.Handle(new GetBankAccountsRequest(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, response.Accounts.Count);

        var first = response.Accounts[0];
        Assert.Equal("ComgateCZK", first.Name);
        Assert.Equal("123456789", first.AccountNumber);
        Assert.Equal(BankClientProvider.Comgate.ToString(), first.Provider);
        Assert.Equal(CurrencyCode.CZK.ToString(), first.Currency);

        var second = response.Accounts[1];
        Assert.Equal("ComgateEUR", second.Name);
        Assert.Equal("987654321", second.AccountNumber);
        Assert.Equal(BankClientProvider.Comgate.ToString(), second.Provider);
        Assert.Equal(CurrencyCode.EUR.ToString(), second.Currency);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GetBankAccountsHandler(null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var settings = new BankAccountSettings { Accounts = new List<BankAccountConfiguration>() };
        Assert.Throws<ArgumentNullException>(() =>
            new GetBankAccountsHandler(Options.Create(settings), null!));
    }
}
