using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Shipments;
using Anela.Heblo.Application.Features.ShipmentLabels;
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

    [Theory]
    [InlineData("canceled")]
    [InlineData("cancel_requested")]
    [InlineData("deleted")]
    [InlineData("request_failed")]
    [InlineData("CANCELED")]
    public async Task GetLabelsByOrderCodeAsync_ExcludesDeadStatusShipments(string deadStatus)
    {
        // Arrange — two shipments: one dead (e.g. canceled), one active (created)
        var activeGuid = Guid.NewGuid();
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        guid = Guid.NewGuid(),
                        orderCode = "0001234",
                        status = deadStatus,
                        packages = new[]
                        {
                            new { name = "Old", labelUrl = "https://x.com/old.pdf", labelZpl = (string?)null, trackingNumber = (string?)null, trackingUrl = (string?)null },
                        },
                    },
                    new
                    {
                        guid = activeGuid,
                        orderCode = "0001234",
                        status = "created",
                        packages = new[]
                        {
                            new { name = "New", labelUrl = "https://x.com/new.pdf", labelZpl = (string?)null, trackingNumber = (string?)null, trackingUrl = (string?)null },
                        },
                    },
                },
            },
            errors = Array.Empty<object>(),
        }));

        // Act
        var result = await client.GetLabelsByOrderCodeAsync("0001234");

        // Assert
        result.Should().HaveCount(1);
        result[0].ShipmentGuid.Should().Be(activeGuid);
        result[0].PackageName.Should().Be("New");
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

    [Fact]
    public async Task GetLatestActiveTrackingNumberAsync_ReturnsTrackingFromLatestActiveShipment_ExcludingDeadStatuses()
    {
        // Arrange — mirrors order 126000035: older cancelled shipments carry tracking,
        // the latest 'created' shipment is the one whose tracking should be backfilled.
        // All packages share the name "Vlastní balení" (non-unique), proving the match
        // is by shipment, not by package name.
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        guid = Guid.NewGuid(),
                        orderCode = "126000035",
                        status = "deleted",
                        packages = new[] { new { name = "Vlastní balení", trackingNumber = (string?)null } },
                    },
                    new
                    {
                        guid = Guid.NewGuid(),
                        orderCode = "126000035",
                        status = "canceled",
                        packages = new[] { new { name = "Vlastní balení", trackingNumber = (string?)"70603624111" } },
                    },
                    new
                    {
                        guid = Guid.NewGuid(),
                        orderCode = "126000035",
                        status = "created",
                        packages = new[] { new { name = "Vlastní balení", trackingNumber = (string?)"70603624124" } },
                    },
                },
            },
            errors = Array.Empty<object>(),
        }));

        // Act
        var result = await client.GetLatestActiveTrackingNumberAsync("126000035");

        // Assert
        result.Should().Be("70603624124");
    }

    [Fact]
    public async Task GetLatestActiveTrackingNumberAsync_ReturnsLastActiveShipment_WhenMultipleActive()
    {
        // Arrange — two active shipments; the latest (last in Shoptet's oldest-first order) wins.
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        guid = Guid.NewGuid(),
                        orderCode = "0001234",
                        status = "created",
                        packages = new[] { new { name = "Vlastní balení", trackingNumber = (string?)"TRK-OLDER" } },
                    },
                    new
                    {
                        guid = Guid.NewGuid(),
                        orderCode = "0001234",
                        status = "created",
                        packages = new[] { new { name = "Vlastní balení", trackingNumber = (string?)"TRK-LATEST" } },
                    },
                },
            },
            errors = Array.Empty<object>(),
        }));

        // Act
        var result = await client.GetLatestActiveTrackingNumberAsync("0001234");

        // Assert
        result.Should().Be("TRK-LATEST");
    }

    [Fact]
    public async Task GetLatestActiveTrackingNumberAsync_ReturnsNull_WhenLatestActiveShipmentHasNoTrackingYet()
    {
        // Arrange — latest active shipment is still 'requested' with no tracking; an older
        // cancelled shipment has tracking but must NOT be used (it's a dead label).
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        guid = Guid.NewGuid(),
                        orderCode = "0001234",
                        status = "canceled",
                        packages = new[] { new { name = "Vlastní balení", trackingNumber = (string?)"TRK-DEAD" } },
                    },
                    new
                    {
                        guid = Guid.NewGuid(),
                        orderCode = "0001234",
                        status = "requested",
                        packages = new[] { new { name = "Vlastní balení", trackingNumber = (string?)null } },
                    },
                },
            },
            errors = Array.Empty<object>(),
        }));

        // Act
        var result = await client.GetLatestActiveTrackingNumberAsync("0001234");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestActiveTrackingNumberAsync_ReturnsFirstNonEmptyTracking_WhenLatestActiveHasMultiplePackages()
    {
        // Arrange — latest active shipment has several packages; the first carries no tracking yet.
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        guid = Guid.NewGuid(),
                        orderCode = "0001234",
                        status = "created",
                        packages = new[]
                        {
                            new { name = "Vlastní balení", trackingNumber = (string?)null },
                            new { name = "Vlastní balení", trackingNumber = (string?)"TRK-SECOND" },
                        },
                    },
                },
            },
            errors = Array.Empty<object>(),
        }));

        // Act
        var result = await client.GetLatestActiveTrackingNumberAsync("0001234");

        // Assert
        result.Should().Be("TRK-SECOND");
    }

    [Fact]
    public async Task GetLatestActiveTrackingNumberAsync_ReturnsNull_WhenNoShipments()
    {
        // Arrange
        var client = BuildClient(_ => Json(new
        {
            data = new { items = Array.Empty<object>() },
            errors = Array.Empty<object>(),
        }));

        // Act
        var result = await client.GetLatestActiveTrackingNumberAsync("0001234");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestActiveTrackingNumberAsync_ReturnsNull_WhenAllShipmentsAreDead()
    {
        // Arrange
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        guid = Guid.NewGuid(),
                        orderCode = "0001234",
                        status = "canceled",
                        packages = new[] { new { name = "Vlastní balení", trackingNumber = (string?)"TRK-DEAD" } },
                    },
                },
            },
            errors = Array.Empty<object>(),
        }));

        // Act
        var result = await client.GetLatestActiveTrackingNumberAsync("0001234");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetShippingOptionsAsync_WithOptions_ReturnsMappedList()
    {
        // Arrange
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                shippingOptions = new[]
                {
                    new { shippingId = 123, methodName = "PPL", carrierCode = "PPL" },
                    new { shippingId = 456, methodName = "Zásilkovna", carrierCode = "ZASILKOVNA" },
                }
            },
            errors = (object?)null,
        }));

        // Act
        var result = await client.GetShippingOptionsAsync("0001234");

        // Assert
        result.Should().HaveCount(2);
        result[0].CarrierCode.Should().Be("123");
        result[0].Name.Should().Be("PPL");
        result[1].CarrierCode.Should().Be("456");
    }

    [Fact]
    public async Task GetShippingOptionsAsync_WithEmptyOptions_ReturnsEmptyList()
    {
        // Arrange
        var client = BuildClient(_ => Json(new
        {
            data = new { shippingOptions = Array.Empty<object>() },
            errors = (object?)null,
        }));

        // Act
        var result = await client.GetShippingOptionsAsync("0001234");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetShippingOptionsAsync_WhenShoptetReturnsNonSuccess_ThrowsHttpRequestException()
    {
        // Arrange
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("Service Unavailable", Encoding.UTF8, "text/plain"),
        });

        // Act
        var act = () => client.GetShippingOptionsAsync("0001234");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*503*");
    }

    [Fact]
    public async Task CreateShipmentAsync_PostsCorrectBodyAndReturnsMappedResult()
    {
        // Arrange
        var shipmentGuid = Guid.NewGuid();
        HttpRequestMessage? capturedRequest = null;

        var client = BuildClient(req =>
        {
            capturedRequest = req;
            return Json(new
            {
                data = new { guid = shipmentGuid, checkUrls = (object?)null },
                errors = (object?)null,
            });
        });

        var command = new CreateShipmentCommand
        {
            OrderCode = "0001234",
            CarrierCode = "123",
            Package = new ShipmentPackage
            {
                WidthMm = 300,
                HeightMm = 200,
                DepthMm = 150,
                WeightGrams = 500,
            }
        };

        // Act
        var result = await client.CreateShipmentAsync(command);

        // Assert
        result.ShipmentGuid.Should().Be(shipmentGuid);
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.PathAndQuery.Should().Be("/api/shipments");

        var body = await capturedRequest.Content!.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        var data = json.GetProperty("data");
        data.GetProperty("orderCode").GetString().Should().Be("0001234");
        data.GetProperty("shippingId").GetInt32().Should().Be(123);
        data.GetProperty("packages")[0].GetProperty("weight").GetString().Should().Be("0.500");
    }

    [Fact]
    public async Task CreateShipmentAsync_WhenShoptetReturnsNonSuccess_ThrowsHttpRequestException()
    {
        // Arrange
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });

        var command = new CreateShipmentCommand
        {
            OrderCode = "0001234",
            CarrierCode = "123",
            Package = new ShipmentPackage { WidthMm = 300, HeightMm = 200, DepthMm = 150, WeightGrams = 500 }
        };

        // Act
        var act = () => client.CreateShipmentAsync(command);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*422*");
    }
}
