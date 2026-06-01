using Anela.Heblo.Application.Features.Bank.Infrastructure;
using Anela.Heblo.Domain.Features.Bank;
using Moq;

namespace Anela.Heblo.Tests.Features.Bank;

public class BankClientFactoryTests
{
    private readonly Mock<IBankClient> _comgateClient;
    private readonly Mock<IBankClient> _shoptetPayClient;

    public BankClientFactoryTests()
    {
        _comgateClient = new Mock<IBankClient>();
        _comgateClient.Setup(x => x.Provider).Returns(BankClientProvider.Comgate);

        _shoptetPayClient = new Mock<IBankClient>();
        _shoptetPayClient.Setup(x => x.Provider).Returns(BankClientProvider.ShoptetPay);
    }

    [Fact]
    public void GetClient_WithComgateProvider_ReturnsComgateClient()
    {
        var factory = new BankClientFactory(new[] { _comgateClient.Object, _shoptetPayClient.Object });
        var config = new BankAccountConfiguration { Provider = BankClientProvider.Comgate };

        var result = factory.GetClient(config);

        Assert.Same(_comgateClient.Object, result);
    }

    [Fact]
    public void GetClient_WithShoptetPayProvider_ReturnsShoptetPayClient()
    {
        var factory = new BankClientFactory(new[] { _comgateClient.Object, _shoptetPayClient.Object });
        var config = new BankAccountConfiguration { Provider = BankClientProvider.ShoptetPay };

        var result = factory.GetClient(config);

        Assert.Same(_shoptetPayClient.Object, result);
    }

    [Fact]
    public void GetClient_WithUnknownProvider_ThrowsInvalidOperationException()
    {
        var factory = new BankClientFactory(new[] { _comgateClient.Object });
        var config = new BankAccountConfiguration { Provider = BankClientProvider.ShoptetPay };

        Assert.Throws<InvalidOperationException>(() => factory.GetClient(config));
    }
}
