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
}
