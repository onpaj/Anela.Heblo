using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;

public class BillingMethodMapper
{
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

    public BillingMethod Map(string? shoptetCode)
    {
        if (string.IsNullOrEmpty(shoptetCode) || !CodeMap.TryGetValue(shoptetCode, out var method))
        {
            if (!string.IsNullOrEmpty(shoptetCode))
                _logger.LogWarning(
                    "Unknown Shoptet billingMethod code '{Code}' — defaulting to BankTransfer. Add to BillingMethodMapper.Map.",
                    shoptetCode);
            return BillingMethod.BankTransfer;
        }
        return method;
    }
}
