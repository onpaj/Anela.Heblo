using System.Net;
using System.Text;
using Anela.Heblo.Adapters.Flexi.Price;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Rem.FlexiBeeSDK.Client;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Flexi;

public class FlexiProductPriceWriterTests
{
    private static (FlexiProductPriceWriter Writer, List<HttpRequestMessage> Requests, List<string> Bodies) Create(
        HttpStatusCode status = HttpStatusCode.OK, string responseBody = "{}") =>
        CreateWithCache(new MemoryCache(new MemoryCacheOptions()), status, responseBody);

    private static (FlexiProductPriceWriter Writer, List<HttpRequestMessage> Requests, List<string> Bodies) CreateWithCache(
        IMemoryCache cache, HttpStatusCode status = HttpStatusCode.OK, string responseBody = "{}")
    {
        var requests = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        var handler = new StubHandler(requests, bodies, status, responseBody);
        var factory = new StubHttpClientFactory(new HttpClient(handler));
        var settings = new FlexiBeeSettings { Server = "https://petra-tesarikova.flexibee.eu", Company = "anela" };

        return (new FlexiProductPriceWriter(factory, settings, cache, NullLogger<FlexiProductPriceWriter>.Instance),
                requests, bodies);
    }

    [Fact]
    public async Task addresses_the_write_by_internal_cenik_id_never_by_code()
    {
        // Arrange
        var (writer, requests, _) = Create();

        // Act
        await writer.SetPriceWithoutVatAsync(147, 157.02m, CancellationToken.None);

        // Assert
        requests.Should().ContainSingle();
        requests[0].Method.Should().Be(HttpMethod.Put);
        requests[0].RequestUri!.AbsolutePath.Should().Be("/c/anela/cenik/147.json");
        requests[0].RequestUri!.ToString().Should().NotContain("code:");
    }

    [Fact]
    public async Task sends_cena_zakl_in_invariant_culture_with_two_decimals()
    {
        // Arrange
        var (writer, _, bodies) = Create();

        // Act
        await writer.SetPriceWithoutVatAsync(147, 157.019m, CancellationToken.None);

        // Assert
        bodies[0].Should().Contain("\"cenaZakl\":\"157.02\"");
        bodies[0].Should().Contain("winstrom").And.Contain("cenik");
    }

    [Fact]
    public async Task never_writes_the_purchase_price()
    {
        // Arrange
        var (writer, _, bodies) = Create();

        // Act
        await writer.SetPriceWithoutVatAsync(147, 157.02m, CancellationToken.None);

        // Assert
        bodies[0].Should().NotContain("cenanakup").And.NotContain("cenaNakup");
    }

    [Fact]
    public async Task rejects_a_non_positive_erp_item_id_without_calling_flexi()
    {
        // Arrange
        var (writer, requests, _) = Create();

        // Act
        var act = () => writer.SetPriceWithoutVatAsync(0, 157.02m, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task throws_with_the_response_body_when_flexi_rejects_the_write()
    {
        // Arrange
        var (writer, _, _) = Create(HttpStatusCode.BadRequest, "{\"winstrom\":{\"success\":\"false\"}}");

        // Act
        var act = () => writer.SetPriceWithoutVatAsync(147, 157.02m, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).And.Message.Should().Contain("success");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public async Task rejects_a_non_positive_price_without_calling_flexi(decimal priceWithoutVat)
    {
        // Arrange
        var (writer, requests, _) = Create();

        // Act
        var act = () => writer.SetPriceWithoutVatAsync(147, priceWithoutVat, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task evicts_the_cached_flexi_price_read_after_a_successful_write()
    {
        // Arrange: the ERP read is served from a 5-minute IMemoryCache entry. Left in place,
        // the comparison screen refetched right after a successful save would read Shoptet
        // live (new price) and Flexi from cache (old price), and render the change the
        // operator just made as a FlexiDiffers divergence.
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set(FlexiProductPriceErpClient.CacheKey, new List<ProductPriceFlexiDto>());
        var (writer, _, _) = CreateWithCache(cache);

        // Act
        await writer.SetPriceWithoutVatAsync(147, 157.02m, CancellationToken.None);

        // Assert
        cache.TryGetValue(FlexiProductPriceErpClient.CacheKey, out _).Should().BeFalse();
    }

    [Fact]
    public async Task keeps_the_cached_flexi_price_read_when_the_write_is_rejected()
    {
        // Arrange: a rejected write changed nothing in Flexi, so the cached read is still true.
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set(FlexiProductPriceErpClient.CacheKey, new List<ProductPriceFlexiDto>());
        var (writer, _, _) = CreateWithCache(cache, HttpStatusCode.BadRequest, "{}");

        // Act
        var act = () => writer.SetPriceWithoutVatAsync(147, 157.02m, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        cache.TryGetValue(FlexiProductPriceErpClient.CacheKey, out _).Should().BeTrue();
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
