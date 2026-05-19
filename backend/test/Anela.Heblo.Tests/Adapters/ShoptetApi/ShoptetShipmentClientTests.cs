using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Shipments;
using FluentAssertions;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetShipmentClientTests
{
    private static ShoptetShipmentClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new FakeDelegatingHandler(handler))
        {
            BaseAddress = new Uri("https://fake.shoptet.cz"),
        };
        return new ShoptetShipmentClient(http);
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
    public async Task GetLabelsByOrderCodeAsync_WithSinglePackage_ReturnsMappedLabel()
    {
        // Arrange
        var shipmentGuid = Guid.NewGuid();
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        guid = shipmentGuid,
                        orderCode = "0001234",
                        packages = new[]
                        {
                            new
                            {
                                name = "Zásilka 1",
                                labelUrl = "https://api.myshoptet.com/label.pdf",
                                labelZpl = "^XA^XZ",
                                trackingNumber = "TRK001",
                                trackingUrl = "https://carrier.cz/TRK001",
                            }
                        },
                    }
                },
            },
            errors = Array.Empty<object>(),
        }));

        // Act
        var result = await client.GetLabelsByOrderCodeAsync("0001234");

        // Assert
        result.Should().HaveCount(1);
        var label = result[0];
        label.ShipmentGuid.Should().Be(shipmentGuid);
        label.OrderCode.Should().Be("0001234");
        label.PackageName.Should().Be("Zásilka 1");
        label.LabelUrl.Should().Be("https://api.myshoptet.com/label.pdf");
        label.LabelZpl.Should().Be("^XA^XZ");
        label.TrackingNumber.Should().Be("TRK001");
        label.TrackingUrl.Should().Be("https://carrier.cz/TRK001");
    }

    [Fact]
    public async Task GetLabelsByOrderCodeAsync_WithMultipleShipmentsAndPackages_ReturnsAllFlattened()
    {
        // Arrange
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        guid = guid1,
                        orderCode = "0001234",
                        packages = new[]
                        {
                            new { name = "P1", labelUrl = "https://x.com/1.pdf", labelZpl = (string?)null, trackingNumber = (string?)null, trackingUrl = (string?)null },
                            new { name = "P2", labelUrl = (string?)null, labelZpl = "^XA^XZ", trackingNumber = (string?)null, trackingUrl = (string?)null },
                        },
                    },
                    new
                    {
                        guid = guid2,
                        orderCode = "0001234",
                        packages = new[]
                        {
                            new { name = "P3", labelUrl = "https://x.com/3.pdf", labelZpl = (string?)null, trackingNumber = (string?)null, trackingUrl = (string?)null },
                        },
                    },
                },
            },
            errors = Array.Empty<object>(),
        }));

        // Act
        var result = await client.GetLabelsByOrderCodeAsync("0001234");

        // Assert
        result.Should().HaveCount(3);
        result.Select(l => l.PackageName).Should().Equal("P1", "P2", "P3");
        result[0].ShipmentGuid.Should().Be(guid1);
        result[2].ShipmentGuid.Should().Be(guid2);
    }

    [Fact]
    public async Task GetLabelsByOrderCodeAsync_WithEmptyItems_ReturnsEmptyList()
    {
        // Arrange
        var client = BuildClient(_ => Json(new
        {
            data = new { items = Array.Empty<object>() },
            errors = Array.Empty<object>(),
        }));

        // Act
        var result = await client.GetLabelsByOrderCodeAsync("0001234");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLabelsByOrderCodeAsync_WhenShoptetReturnsNonSuccess_ThrowsHttpRequestException()
    {
        // Arrange
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("Service Unavailable", Encoding.UTF8, "text/plain"),
        });

        // Act
        var act = () => client.GetLabelsByOrderCodeAsync("0001234");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*503*");
    }

    [Fact]
    public async Task GetLabelsByOrderCodeAsync_WhenShoptetReturnsErrorsArray_ThrowsHttpRequestException()
    {
        // Arrange
        var client = BuildClient(_ => Json(new
        {
            data = (object?)null,
            errors = new[]
            {
                new { errorCode = "shipment-not-found", message = "Shipment not found", instance = "/api/shipments?orderCode=0001234" }
            },
        }));

        // Act
        var act = () => client.GetLabelsByOrderCodeAsync("0001234");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Shipment not found*");
    }

    [Fact]
    public async Task GetLabelsByOrderCodeAsync_UsesCorrectQueryString()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var client = BuildClient(req =>
        {
            capturedRequest = req;
            return Json(new { data = new { items = Array.Empty<object>() }, errors = Array.Empty<object>() });
        });

        // Act
        await client.GetLabelsByOrderCodeAsync("MY-ORDER-CODE");

        // Assert
        capturedRequest!.RequestUri!.PathAndQuery.Should().Be("/api/shipments?orderCode=MY-ORDER-CODE");
    }

    [Fact]
    public async Task GetLabelsByOrderCodeAsync_EncodesOrderCodeInQueryString()
    {
        // Arrange — order code with a space (must become %20 in the URL)
        HttpRequestMessage? capturedRequest = null;
        var client = BuildClient(req =>
        {
            capturedRequest = req;
            return Json(new { data = new { items = Array.Empty<object>() }, errors = Array.Empty<object>() });
        });

        // Act
        await client.GetLabelsByOrderCodeAsync("ORDER 01");

        // Assert
        capturedRequest!.RequestUri!.PathAndQuery.Should().Be("/api/shipments?orderCode=ORDER%2001");
    }
}
