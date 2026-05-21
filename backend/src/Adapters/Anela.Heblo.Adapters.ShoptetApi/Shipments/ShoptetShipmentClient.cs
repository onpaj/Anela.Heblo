using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;
using Anela.Heblo.Application.Features.ShipmentLabels;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments;

public class ShoptetShipmentClient : IShipmentClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ShoptetShipmentClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<ShipmentLabel>> GetLabelsByOrderCodeAsync(
        string orderCode,
        CancellationToken ct = default)
    {
        var encodedOrderCode = Uri.EscapeDataString(orderCode);
        var response = await _http.GetAsync($"/api/shipments?orderCode={encodedOrderCode}", ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"GET /api/shipments?orderCode={orderCode} returned {(int)response.StatusCode}: {body}");
        }

        var data = await response.Content.ReadFromJsonAsync<ShoptetShipmentListResponse>(JsonOptions, ct);

        if (data?.Errors is { Count: > 0 })
        {
            var errorMsg = string.Join("; ", data.Errors.Select(e => e.Message));
            throw new HttpRequestException($"Shoptet Delivery API error for order {orderCode}: {errorMsg}");
        }

        var items = data?.Data?.Items ?? [];

        return items
            .SelectMany(shipment => (shipment.Packages ?? [])
                .Select(pkg => new ShipmentLabel
                {
                    ShipmentGuid = shipment.Guid,
                    OrderCode = orderCode,
                    PackageName = pkg.Name ?? string.Empty,
                    LabelUrl = pkg.LabelUrl,
                    LabelZpl = pkg.LabelZpl,
                    TrackingNumber = pkg.TrackingNumber,
                    TrackingUrl = pkg.TrackingUrl,
                }))
            .ToList();
    }

    public async Task<IReadOnlyList<ShippingOption>> GetShippingOptionsAsync(
        string orderCode,
        CancellationToken ct = default)
    {
        var encodedOrderCode = Uri.EscapeDataString(orderCode);
        var response = await _http.GetAsync(
            $"/api/shipments/order/{encodedOrderCode}/shipping-options", ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"GET shipping-options for order {orderCode} returned {(int)response.StatusCode}: {body}");
        }

        var data = await response.Content.ReadFromJsonAsync<ShoptetShippingOptionsResponse>(JsonOptions, ct);

        if (data?.Errors is { Count: > 0 })
        {
            var errorMsg = string.Join("; ", data.Errors.Select(e => e.Message ?? e.ErrorCode ?? "unknown"));
            throw new HttpRequestException(
                $"Shoptet Delivery API error for order {orderCode}: {errorMsg}");
        }

        var options = data?.Data?.ShippingOptions ?? [];

        return options
            .Select(o => new ShippingOption
            {
                CarrierCode = o.ShippingId.ToString(),
                Name = o.MethodName ?? o.CarrierCode ?? string.Empty,
            })
            .ToList();
    }

    public async Task<CreatedShipment> CreateShipmentAsync(
        CreateShipmentCommand command,
        CancellationToken ct = default)
    {
        if (!int.TryParse(command.CarrierCode, out var shippingId))
        {
            throw new ArgumentException(
                $"CarrierCode must be a parseable integer (shippingId) but got: {command.CarrierCode}");
        }

        var weightKg = (command.Package.WeightGrams / 1000.0).ToString("F3",
            System.Globalization.CultureInfo.InvariantCulture);

        var envelope = new ShoptetCreateShipmentRequestEnvelope
        {
            Data = new ShoptetCreateShipmentRequestData
            {
                OrderCode = command.OrderCode,
                ShippingId = shippingId,
                Packages =
                [
                    new ShoptetCreatePackageDto
                    {
                        Width = command.Package.WidthMm,
                        Height = command.Package.HeightMm,
                        Depth = command.Package.DepthMm,
                        Weight = weightKg,
                    }
                ],
            }
        };

        var response = await _http.PostAsJsonAsync("/api/shipments", envelope, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"POST /api/shipments for order {command.OrderCode} returned {(int)response.StatusCode}: {body}");
        }

        var result = await response.Content.ReadFromJsonAsync<ShoptetCreateShipmentResponse>(JsonOptions, ct);

        if (result?.Errors is { Count: > 0 })
        {
            var errorMsg = string.Join("; ", result.Errors.Select(e => e.Message ?? e.ErrorCode ?? "unknown"));
            throw new HttpRequestException(
                $"Shoptet Delivery API error creating shipment for order {command.OrderCode}: {errorMsg}");
        }

        return new CreatedShipment
        {
            ShipmentGuid = result?.Data?.Guid ?? Guid.Empty,
            Status = null,
        };
    }

    public async Task DeleteShipmentAsync(Guid shipmentGuid, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/shipments/{shipmentGuid}", ct);

        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Shoptet DELETE /api/shipments/{shipmentGuid} failed ({response.StatusCode}): {content}");
        }
    }
}
