using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Application.Features.ShoptetOrders;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetOrderClientTests
{
    private static ShoptetOrderClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new FakeDelegatingHandler(handler))
        {
            BaseAddress = new Uri("https://fake.shoptet.cz"),
        };
        return new ShoptetOrderClient(http, Options.Create(new ShoptetOrdersSettings()));
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

    [Fact]
    public async Task ListOrdersByStatusAsync_PaginatesAndMapsAllOrders()
    {
        var client = BuildClient(request =>
        {
            var page = request.RequestUri!.Query.Contains("page=2") ? "2" : "1";
            if (page == "1")
            {
                return Json(new
                {
                    data = new
                    {
                        orders = new[]
                        {
                            new { code = "ORD-1", externalCode = "EXT-1", email = "a@example.com", status = new { id = 70 } },
                        },
                        paginator = new { pageCount = 2, page = 1, totalCount = 2 },
                    },
                });
            }

            return Json(new
            {
                data = new
                {
                    orders = new[]
                    {
                        new { code = "ORD-2", externalCode = (string?)null, email = "b@example.com", status = new { id = 70 } },
                    },
                    paginator = new { pageCount = 2, page = 2, totalCount = 2 },
                },
            });
        });

        var result = await client.ListOrdersByStatusAsync(70);

        result.Should().HaveCount(2);
        result[0].Code.Should().Be("ORD-1");
        result[0].ExternalCode.Should().Be("EXT-1");
        result[0].Email.Should().Be("a@example.com");
        result[0].StatusId.Should().Be(70);
        result[1].Code.Should().Be("ORD-2");
    }

    [Fact]
    public async Task AppendEshopRemarkAsync_SetsNoteAsFirstLine_WhenNoExistingRemark()
    {
        string? patchedRemark = null;
        var client = BuildClient(req =>
        {
            if (req.Method == HttpMethod.Get)
                return Json(new { data = new { order = new { notes = (object?)null } } });

            patchedRemark = req.Content!.ReadAsStringAsync().Result;
            return Json(new { data = (object?)null });
        });

        await client.AppendEshopRemarkAsync("ORDER-1", "fraud suspicion");

        patchedRemark.Should().Contain("fraud suspicion");
    }

    [Fact]
    public async Task AppendEshopRemarkAsync_AppendsWithNewline_WhenExistingRemarkPresent()
    {
        string? patchedRemark = null;
        var client = BuildClient(req =>
        {
            if (req.Method == HttpMethod.Get)
                return Json(new { data = new { order = new { notes = new { eshopRemark = "previous staff note" } } } });

            patchedRemark = req.Content!.ReadAsStringAsync().Result;
            return Json(new { data = (object?)null });
        });

        await client.AppendEshopRemarkAsync("ORDER-1", "blocked by accounting");

        patchedRemark.Should().Contain("previous staff note\\nblocked by accounting");
    }
}
