using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Customers;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.ShoptetCustomers;

public class ShoptetCustomerClientTests
{
    private static ShoptetCustomerClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new FakeHandler(handler))
        {
            BaseAddress = new Uri("https://api.myshoptet.com"),
        };
        return new ShoptetCustomerClient(http);
    }

    private static HttpResponseMessage Json(object obj, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task GetCustomerByGuidAsync_WithFullResponse_MappsFieldsCorrectly()
    {
        // Arrange
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                customer = new
                {
                    guid = "test-guid",
                    email = "jana@test.cz",
                    fullName = "Jana Nováková",
                    customerGroup = new { id = 1, name = "VIP" },
                    priceList = new { id = 2, name = "Retail" },
                    billingAddress = new
                    {
                        fullName = "Jana Nováková",
                        street = "Ulice 5",
                        city = "Praha",
                        zip = "12000",
                        countryCode = "CZ",
                    },
                },
            },
        }));

        // Act
        var result = await client.GetCustomerByGuidAsync("test-guid");

        // Assert
        result.Should().NotBeNull();
        result!.Guid.Should().Be("test-guid");
        result.FullName.Should().Be("Jana Nováková");
        result.Email.Should().Be("jana@test.cz");
        result.CustomerGroup.Should().Be("VIP");
        result.PriceList.Should().Be("Retail");
        result.DefaultShippingAddress.Should().Be("CZ, Praha, 12000, Ulice 5");
    }

    [Fact]
    public async Task GetCustomerByGuidAsync_When404_ReturnsNull()
    {
        // Arrange
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not Found", Encoding.UTF8, "text/plain"),
        });

        // Act
        var result = await client.GetCustomerByGuidAsync("nonexistent-guid");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCustomerByGuidAsync_When500_ThrowsHttpRequestExceptionWithStatusCode()
    {
        // Arrange
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal Server Error", Encoding.UTF8, "text/plain"),
        });

        // Act
        var act = () => client.GetCustomerByGuidAsync("some-guid");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*500*");
    }

    [Fact]
    public async Task GetCustomerByGuidAsync_WhenCustomerIsNull_ReturnsNull()
    {
        // Arrange
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                customer = (object?)null,
            },
        }));

        // Act
        var result = await client.GetCustomerByGuidAsync("some-guid");

        // Assert
        result.Should().BeNull();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
