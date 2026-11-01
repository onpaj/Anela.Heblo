using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Products;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetProductClientTests
{
    private const string Guid1 = "d4202382-2898-11e9-beb1-002590dad85e";
    private const string Guid2 = "2c114f22-289b-11e9-beb1-002590dad85e";
    private const string ImageBase = "https://shop.test/user/shop/orig/";

    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            handler(request);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Serialize(object o) => JsonSerializer.Serialize(o);

    private static string ListJson(int page, int pageCount, params (string guid, string name, string visibility)[] items) =>
        Serialize(new
        {
            data = new
            {
                products = items.Select(i => new { guid = i.guid, name = i.name, visibility = i.visibility }),
                paginator = new { totalCount = items.Length, page, pageCount },
            },
        });

    private static object Variant(string code, string? image, string weight, bool visible = true) => new
    {
        code,
        name = (string?)null,
        visible,
        image,
        weight,
        width = "0.0",
        height = "0.0",
        depth = "0.0",
        atypicalShipping = false,
    };

    private static object Image(string name, string seoName, bool isMain) => new { name, seoName, isMainImage = isMain };

    private static string DetailJson(string guid, string name, object[] variants, object[]? images = null, object? extra = null) =>
        Serialize(new
        {
            data = new
            {
                guid,
                name,
                visibility = "visible",
                variants,
                images = images ?? Array.Empty<object>(),
                flags = extra,
            },
        });

    private static readonly string CutsJson = Serialize(new
    {
        data = new { imageCuts = new[] { new { name = "detail", urlPath = "https://shop.test/user/shop/detail/" }, new { name = "orig", urlPath = ImageBase } } },
    });

    private static readonly string FlagsJson = Serialize(new
    {
        data = new { flags = new[] { new { code = "bestseller", title = "BESTSELLER" }, new { code = "new", title = "NOVINKA" } } },
    });

    private static readonly string EmptyList = ListJson(1, 1);

    /// <summary>Routes by path + query; unmatched requests fail the test loudly.</summary>
    private static Func<HttpRequestMessage, Task<HttpResponseMessage>> Router(
        Dictionary<string, string> routes, Func<string, HttpResponseMessage?>? overrides = null) =>
        request =>
        {
            var key = request.RequestUri!.PathAndQuery;
            var overridden = overrides?.Invoke(key);
            if (overridden is not null)
            {
                return Task.FromResult(overridden);
            }
            return Task.FromResult(routes.TryGetValue(key, out var body)
                ? Json(body)
                : Json("{\"errors\":[{\"message\":\"unrouted " + key + "\"}]}", HttpStatusCode.NotFound));
        };

    private static Dictionary<string, string> BaseRoutes() => new()
    {
        ["/api/eshop?include=imageCuts"] = CutsJson,
        ["/api/products/flags"] = FlagsJson,
        ["/api/products?flag=bestseller&itemsPerPage=1000&page=1"] = EmptyList,
        ["/api/products?flag=new&itemsPerPage=1000&page=1"] = EmptyList,
    };

    private static ShoptetProductClient BuildClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://fake.shoptet.cz") },
            Options.Create(new ShoptetProductClientOptions { MinRequestIntervalMs = 0 }),
            NullLogger<ShoptetProductClient>.Instance);

    private static ShoptetProductClient BuildClient(Dictionary<string, string> routes, Func<string, HttpResponseMessage?>? overrides = null) =>
        BuildClient(new AsyncHandler(Router(routes, overrides)));

    private static Dictionary<string, string> SingleProductRoutes(string detailJson, string guid = Guid1)
    {
        var routes = BaseRoutes();
        routes["/api/products?itemsPerPage=1000&page=1"] = ListJson(1, 1, (guid, "Ochráním zadečky", "visible"));
        routes[$"/api/products/{guid}?include=images"] = detailJson;
        return routes;
    }

    [Fact]
    public async Task ListAsync_MapsVariantFieldsFromDetail()
    {
        var detail = DetailJson(Guid1, "Ochráním zadečky",
            [Variant("OCH001005", "51-1.jpg", "0.035"), Variant("OCH001030", "51-4.jpg", "0.071", visible: false)],
            [Image("51-1.jpg", "51-1_x.jpg", false), Image("51-4.jpg", "51-4_x.jpg", false), Image("51.jpg", "51_main.jpg", true)]);
        var client = BuildClient(SingleProductRoutes(detail));

        var result = await client.ListAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        var first = result.Single(p => p.Code == "OCH001005");
        first.ProductGuid.Should().Be(Guid1);
        first.Name.Should().Be("Ochráním zadečky");
        first.WeightGrams.Should().Be(35);
        first.IsVisible.Should().BeTrue();
        first.ImageUrl.Should().Be("https://shop.test/user/shop/orig/51-1_x.jpg");
        first.DefaultImageUrl.Should().Be("https://shop.test/user/shop/orig/51_main.jpg");
        first.Height.Should().BeNull();
        var second = result.Single(p => p.Code == "OCH001030");
        second.WeightGrams.Should().Be(71);
        second.IsVisible.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_WeightIsRoundedToExactGrams()
    {
        var detail = DetailJson(Guid1, "P", [Variant("A", null, "1.005")]);
        var client = BuildClient(SingleProductRoutes(detail));

        var result = await client.ListAsync(CancellationToken.None);

        result.Single().WeightGrams.Should().Be(1005);
    }

    [Fact]
    public async Task ListAsync_HiddenProduct_MakesVariantInvisible()
    {
        var routes = SingleProductRoutes(DetailJson(Guid1, "P", [Variant("A", null, "0.1")]));
        routes[$"/api/products/{Guid1}?include=images"] = Serialize(new
        {
            data = new { guid = Guid1, name = "P", visibility = "hidden", variants = new[] { Variant("A", null, "0.1") } },
        });
        var client = BuildClient(routes);

        var result = await client.ListAsync(CancellationToken.None);

        result.Single().IsVisible.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_FlagsComeFromFlagFilter_NotFromDetailConfiguration()
    {
        var detail = DetailJson(Guid1, "P", [Variant("A", null, "0.1")],
            extra: new[] { new { code = "new", dateTo = "2019-10-31" } });
        var routes = SingleProductRoutes(detail);
        routes["/api/products?flag=bestseller&itemsPerPage=1000&page=1"] = ListJson(1, 1, (Guid1, "P", "visible"));
        var client = BuildClient(routes);

        var result = await client.ListAsync(CancellationToken.None);

        result.Single().Flags.Should().BeEquivalentTo(
            [new { Code = "bestseller", Title = "BESTSELLER" }]);
    }

    [Fact]
    public async Task ListAsync_VariantWithoutImage_UsesMainImage()
    {
        var detail = DetailJson(Guid1, "P", [Variant("A", null, "0.1")],
            [Image("51-1.jpg", "51-1_x.jpg", false), Image("51.jpg", "51_main.jpg", true)]);
        var client = BuildClient(SingleProductRoutes(detail));

        var result = await client.ListAsync(CancellationToken.None);

        result.Single().ImageUrl.Should().Be("https://shop.test/user/shop/orig/51_main.jpg");
    }

    [Fact]
    public async Task ListAsync_ZeroWeight_MapsToNull()
    {
        var detail = DetailJson(Guid1, "P", [Variant("A", null, "0.000")]);
        var client = BuildClient(SingleProductRoutes(detail));

        var result = await client.ListAsync(CancellationToken.None);

        result.Single().WeightGrams.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_ProductListHasTwoPages_ReadsBoth()
    {
        var routes = BaseRoutes();
        routes["/api/products?itemsPerPage=1000&page=1"] = ListJson(1, 2, (Guid1, "P1", "visible"));
        routes["/api/products?itemsPerPage=1000&page=2"] = ListJson(2, 2, (Guid2, "P2", "visible"));
        routes[$"/api/products/{Guid1}?include=images"] = DetailJson(Guid1, "P1", [Variant("A", null, "0.1")]);
        routes[$"/api/products/{Guid2}?include=images"] = DetailJson(Guid2, "P2", [Variant("B", null, "0.2")]);
        var client = BuildClient(routes);

        var result = await client.ListAsync(CancellationToken.None);

        result.Select(p => p.Code).Should().BeEquivalentTo("A", "B");
    }

    [Fact]
    public async Task ListAsync_DetailCallFailsAfterRetries_Throws()
    {
        var routes = SingleProductRoutes(DetailJson(Guid1, "P", [Variant("A", null, "0.1")]));
        var client = BuildClient(routes, key =>
            key.StartsWith("/api/products/" + Guid1) ? Json("{}", HttpStatusCode.InternalServerError) : null);

        var act = () => client.ListAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ListAsync_RequestsAreSequential()
    {
        var routes = BaseRoutes();
        routes["/api/products?itemsPerPage=1000&page=1"] = ListJson(1, 1, (Guid1, "P1", "visible"), (Guid2, "P2", "visible"));
        routes[$"/api/products/{Guid1}?include=images"] = DetailJson(Guid1, "P1", [Variant("A", null, "0.1")]);
        routes[$"/api/products/{Guid2}?include=images"] = DetailJson(Guid2, "P2", [Variant("B", null, "0.2")]);
        var router = Router(routes);
        var inFlight = 0;
        var maxInFlight = 0;
        var handler = new AsyncHandler(async request =>
        {
            var current = Interlocked.Increment(ref inFlight);
            maxInFlight = Math.Max(maxInFlight, current);
            await Task.Delay(5);
            var response = await router(request);
            Interlocked.Decrement(ref inFlight);
            return response;
        });
        var client = BuildClient(handler);

        var result = await client.ListAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        maxInFlight.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_MinRequestInterval_SpacesRequestStarts()
    {
        var starts = new List<DateTime>();
        var router = Router(SingleProductRoutes(DetailJson(Guid1, "P", [Variant("A", null, "0.1")])));
        var handler = new AsyncHandler(request =>
        {
            starts.Add(DateTime.UtcNow);
            return router(request);
        });
        var client = new ShoptetProductClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://fake.shoptet.cz") },
            Options.Create(new ShoptetProductClientOptions { MinRequestIntervalMs = 40 }),
            NullLogger<ShoptetProductClient>.Instance);

        await client.ListAsync(CancellationToken.None);

        starts.Should().HaveCountGreaterThan(3);
        for (var i = 1; i < starts.Count; i++)
        {
            (starts[i] - starts[i - 1]).TotalMilliseconds.Should().BeGreaterOrEqualTo(30);
        }
    }
}
