using Anela.Heblo.Adapters.ShoptetApi.ShoptetPay;
using Anela.Heblo.Domain.Features.Bank;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Bank;

public class ShoptetPayBankClientTests
{
    private readonly Mock<ILogger<ShoptetPayBankClient>> _loggerMock;
    private readonly ShoptetPaySettings _settings;

    public ShoptetPayBankClientTests()
    {
        _loggerMock = new Mock<ILogger<ShoptetPayBankClient>>();
        _settings = new ShoptetPaySettings
        {
            ApiToken = "test-token",
            BaseUrl = "https://api.shoptetpay.com"
        };
    }

    [Fact]
    public void Provider_ReturnsShoptetPay()
    {
        var client = new ShoptetPayBankClient(
            new HttpClient(),
            Options.Create(_settings),
            _loggerMock.Object);

        Assert.Equal(BankClientProvider.ShoptetPay, client.Provider);
    }
}
