using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.Flexi.Price;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Rem.FlexiBeeSDK.Client;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Flexi;

public class FlexiPurchasePriceWriterTests
{
    private static (FlexiPurchasePriceWriter Writer, List<HttpRequestMessage> Requests, List<string> Bodies) Create(
        IMemoryCache? cache = null, HttpStatusCode status = HttpStatusCode.OK, string responseBody = "{}")
    {
        var requests = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        var handler = new StubHandler(requests, bodies, status, responseBody);
        var factory = new StubHttpClientFactory(new HttpClient(handler));
        var settings = new FlexiBeeSettings { Server = "https://petra-tesarikova.flexibee.eu", Company = "anela" };

        return (new FlexiPurchasePriceWriter(
                    factory, settings, cache ?? new MemoryCache(new MemoryCacheOptions()),
                    NullLogger<FlexiPurchasePriceWriter>.Instance),
                requests, bodies);
    }

    [Fact]
    public async Task addresses_the_write_by_internal_cenik_id_with_put()
    {
        // Arrange
        var (writer, requests, _) = Create();

        // Act
        await writer.SetPurchasePriceAsync(789, 0.311m, CancellationToken.None);

        // Assert
        requests.Should().ContainSingle();
        requests[0].Method.Should().Be(HttpMethod.Put);
        requests[0].RequestUri!.ToString()
            .Should().Be("https://petra-tesarikova.flexibee.eu/c/anela/cenik/789.json");
    }

    [Fact]
    public async Task sends_only_nakupCena_in_invariant_format()
    {
        // Arrange
        var (writer, _, bodies) = Create();

        // Act
        await writer.SetPurchasePriceAsync(789, 0.311234m, CancellationToken.None);

        // Assert
        using var doc = JsonDocument.Parse(bodies.Single());
        var cenik = doc.RootElement.GetProperty("winstrom").GetProperty("cenik");
        cenik.GetProperty("nakupCena").GetString().Should().Be("0.311234");
        cenik.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "nakupCena" });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task rejects_a_non_positive_id_without_calling_flexi(int erpItemId)
    {
        // Arrange
        var (writer, requests, _) = Create();

        // Act
        var act = () => writer.SetPurchasePriceAsync(erpItemId, 1m, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public async Task rejects_a_non_positive_price_without_calling_flexi(decimal price)
    {
        // Arrange
        var (writer, requests, _) = Create();

        // Act
        var act = () => writer.SetPurchasePriceAsync(789, price, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task throws_with_the_flexi_body_when_the_write_is_rejected()
    {
        // Arrange
        var (writer, _, _) = Create(status: HttpStatusCode.BadRequest, responseBody: "{\"err\":\"nope\"}");

        // Act
        var act = () => writer.SetPurchasePriceAsync(789, 1m, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).And.Message.Should().Contain("nope");
    }

    [Fact]
    public async Task evicts_the_cached_flexi_price_read_after_a_successful_write()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set(FlexiProductPriceErpClient.CacheKey, new List<ProductPriceFlexiDto>());
        var (writer, _, _) = Create(cache);

        // Act
        await writer.SetPurchasePriceAsync(789, 0.311m, CancellationToken.None);

        // Assert
        cache.TryGetValue(FlexiProductPriceErpClient.CacheKey, out _).Should().BeFalse();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public StubHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests;
        private readonly List<string> _bodies;
        private readonly HttpStatusCode _status;
        private readonly string _responseBody;

        public StubHandler(List<HttpRequestMessage> requests, List<string> bodies,
                           HttpStatusCode status, string responseBody)
        {
            _requests = requests;
            _bodies = bodies;
            _status = status;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            if (request.Content is not null)
            {
                _bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
