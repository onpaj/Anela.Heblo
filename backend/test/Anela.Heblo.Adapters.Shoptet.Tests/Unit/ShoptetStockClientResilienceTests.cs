using System.Net;
using System.Text;
using Anela.Heblo.Adapters.ShoptetApi;
using Anela.Heblo.Adapters.ShoptetApi.Stock;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetStockClientResilienceTests
{
    private const string CsvSample =
        "CODE;PAIR;NAME;IMG;IMG2;X;X;X;X;X;X;X;X;X;X;NS;X;X;X;X;X;X;X;X;X;STOCK;LOC;W;H;D;WD;AT\n" +
        "P001;;Product 1;;;;;;;;;;;;;;;;;;;;;;;5;A1;100;10;10;10;0\n";

    private static IServiceProvider BuildProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        Action<ShoptetStockClientOptions>? configureOptions = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var services = new ServiceCollection();
        services.AddLogging();

        var configBuilder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ShoptetApi:BaseUrl"] = "https://api.myshoptet.com/",
            ["ShoptetApi:ApiToken"] = "test-token",
            ["ShoptetApi:StockId"] = "1",
            ["StockClient:Url"] = "https://csv.example.com/export?token=secret-token-123",
            ["StockClient:TimeoutSeconds"] = "1",
            ["StockClient:MaxRetryAttempts"] = "3",
            ["StockClient:RetryBaseDelaySeconds"] = "0"
        });
        var configuration = configBuilder.Build();

        services.AddShoptetApiAdapter(configuration);
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        services.AddHttpClient("ShoptetStockCsv")
            .ConfigurePrimaryHttpMessageHandler(() => new DelegatingStubHandler(handler));

        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage CsvOk() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(CsvSample, Encoding.GetEncoding("windows-1250"))
    };

    private static HttpResponseMessage TransientFailure() => new(HttpStatusCode.ServiceUnavailable)
    {
        Content = new StringContent("transient")
    };

    [Fact]
    public async Task ListAsync_RetriesOnTransient503_AndSucceedsOnThirdAttempt()
    {
        // Arrange
        var calls = 0;
        var provider = BuildProvider((req, ct) =>
        {
            calls++;
            return Task.FromResult(calls < 3 ? TransientFailure() : CsvOk());
        });
        var client = provider.GetRequiredService<IEshopStockClient>();

        // Act
        var result = await client.ListAsync(CancellationToken.None);

        // Assert
        calls.Should().Be(3);
        result.Should().HaveCount(1);
        result[0].Code.Should().Be("P001");
    }

    [Fact]
    public async Task ListAsync_ExhaustsRetries_AndThrowsHttpRequestException()
    {
        // Arrange
        var calls = 0;
        var provider = BuildProvider((req, ct) =>
        {
            calls++;
            return Task.FromResult(TransientFailure());
        });
        var client = provider.GetRequiredService<IEshopStockClient>();

        // Act
        Func<Task> act = async () => await client.ListAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        // 1 initial attempt + 3 retries = 4 total
        calls.Should().Be(4);
    }

    private sealed class DelegatingStubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public DelegatingStubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
