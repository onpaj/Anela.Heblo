using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;

public class ShippingMethodMapper
{
    private readonly ShoptetApiSettings _settings;
    private readonly ILogger<ShippingMethodMapper> _logger;

    public ShippingMethodMapper(IOptions<ShoptetApiSettings> settings)
        : this(settings, NullLogger<ShippingMethodMapper>.Instance)
    {
    }

    public ShippingMethodMapper(IOptions<ShoptetApiSettings> settings, ILogger<ShippingMethodMapper> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public ShippingMethod Map(ShoptetInvoiceShippingDto? shipping)
    {
        var guid = shipping?.Guid;
        if (string.IsNullOrEmpty(guid))
            return ShippingMethod.PickUp;

        if (_settings.InvoiceShippingGuidMap.TryGetValue(guid, out var method))
            return method;

        _logger.LogWarning(
            "Unknown invoice shipping GUID '{Guid}' — defaulting to PickUp. Add to Shoptet:InvoiceShippingGuidMap config.",
            guid);
        return ShippingMethod.PickUp;
    }
}
