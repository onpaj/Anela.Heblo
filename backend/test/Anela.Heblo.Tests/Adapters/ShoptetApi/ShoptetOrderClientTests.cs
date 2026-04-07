using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using FluentAssertions;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetOrderClientTests
{
    private static ShoptetOrderClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new FakeDelegatingHandler(handler))
        {
            BaseAddress = new Uri("https://fake.shoptet.cz"),
        };
        return new ShoptetOrderClient(http);
    }

    private static HttpResponseMessage Json(object obj)
    {
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task GetOrdersByStatusAsync_ReturnsFullHeaderData()
    {
        // Arrange — response matches what Shoptet GET /api/orders actually returns
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                orders = new[]
                {
                    new
                    {
                        code = "ABC001",
                        email = "test@example.com",
                        fullName = "Jan Novak",
                        phone = "+420721000001",
                        company = "Acme s.r.o.",
                        creationTime = "2024-06-01T10:00:00",
                        changeTime = "2024-06-02T08:30:00",
                        paid = true,
                        status = new { id = 5 },
                        shipping = new { guid = "f6610d4d-578d-11e9-beb1-002590dad85e", name = "Zásilkovna (do ruky)" },
                        paymentMethod = new { guid = "6f2c8e36-3faf-11e2-a723-705ab6a2ba75", name = "Platba převodem" },
                    },
                },
                paginator = new { pageCount = 1, page = 1, totalCount = 1 },
            },
        }));

        // Act
        var result = await client.GetOrdersByStatusAsync(5, 1);

        // Assert
        result.Data.Orders.Should().HaveCount(1);
        var order = result.Data.Orders[0];
        order.Code.Should().Be("ABC001");
        order.Email.Should().Be("test@example.com");
        order.FullName.Should().Be("Jan Novak");
        order.Phone.Should().Be("+420721000001");
        order.Company.Should().Be("Acme s.r.o.");
        order.CreationTime.Should().Be("2024-06-01T10:00:00");
        order.ChangeTime.Should().Be("2024-06-02T08:30:00");
        order.Paid.Should().Be(true);
        order.Status.Id.Should().Be(5);
        order.Shipping!.Guid.Should().Be("f6610d4d-578d-11e9-beb1-002590dad85e");
        order.Shipping!.Name.Should().Be("Zásilkovna (do ruky)");
        order.PaymentMethod!.Guid.Should().Be("6f2c8e36-3faf-11e2-a723-705ab6a2ba75");
        order.PaymentMethod!.Name.Should().Be("Platba převodem");
    }

    [Fact]
    public async Task GetOrdersByStatusAsync_ReturnsCorrectPaginator()
    {
        // Arrange
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                orders = Array.Empty<object>(),
                paginator = new { pageCount = 3, page = 2, totalCount = 120 },
            },
        }));

        // Act
        var result = await client.GetOrdersByStatusAsync(5, 2);

        // Assert
        result.Data.Paginator.PageCount.Should().Be(3);
    }
}
