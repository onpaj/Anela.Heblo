using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;

public class BillingMethodMapper
{
    // Stable, documented numeric billing method ids returned by the Shoptet Invoices API.
    // See docs/integrations/shoptet-api.md §10.6 — preferred over the store-configured `name`.
    private static readonly Dictionary<int, BillingMethod> IdMap = new()
    {
        [1] = BillingMethod.CoD,          // Dobírka
        [2] = BillingMethod.BankTransfer, // Převodem
        [3] = BillingMethod.Cash,         // Hotově
        [4] = BillingMethod.CreditCard,   // Kartou
    };

    private static readonly Dictionary<string, BillingMethod> CodeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bankTransfer"] = BillingMethod.BankTransfer,
        // Czech-language billing method names returned by the Shoptet REST API
        ["Převodem"] = BillingMethod.BankTransfer,
        ["cash"] = BillingMethod.Cash,
        ["cashOnDelivery"] = BillingMethod.CoD,
        ["creditCard"] = BillingMethod.CreditCard,
        // "Kartou" is the Czech-language billing method name the REST API returns for card payments.
        ["Kartou"] = BillingMethod.CreditCard,
        ["comgate"] = BillingMethod.Comgate,
    };

    private readonly ILogger<BillingMethodMapper> _logger;

    public BillingMethodMapper(ILogger<BillingMethodMapper> logger)
    {
        _logger = logger;
    }

    public BillingMethodMapper() : this(NullLogger<BillingMethodMapper>.Instance)
    {
    }

    public BillingMethod Map(ShoptetBillingMethodDto? billingMethod)
    {
        if (billingMethod == null)
            return BillingMethod.BankTransfer;

        if (IdMap.TryGetValue(billingMethod.Id, out var byId))
            return byId;

        // Unexpected id (e.g. 0 = missing) — fall back to the store-configured name.
        if (!string.IsNullOrEmpty(billingMethod.Name) && CodeMap.TryGetValue(billingMethod.Name, out var byName))
            return byName;

        _logger.LogWarning(
            "Unknown Shoptet billingMethod id '{Id}' / name '{Name}' — defaulting to BankTransfer. Add to BillingMethodMapper.",
            billingMethod.Id, billingMethod.Name);
        return BillingMethod.BankTransfer;
    }

}
