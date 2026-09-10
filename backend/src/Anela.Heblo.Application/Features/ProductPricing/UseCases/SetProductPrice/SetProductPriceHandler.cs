using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

/// <summary>
/// Writes a retail price through to Shoptet (the source of truth) and then to Flexi.
///
/// The Flexi pre-flight runs BEFORE the Shoptet write on purpose: a missing ceník id or VAT
/// rate is knowable up front and guarantees the Flexi leg cannot succeed, so discovering it
/// afterwards would manufacture an avoidable divergence. The only partial failure this can
/// produce is a genuine Flexi write error, which is not knowable in advance.
/// </summary>
public class SetProductPriceHandler : IRequestHandler<SetProductPriceRequest, SetProductPriceResponse>
{
    private const int PriceDecimals = 2;

    private readonly IEshopPriceListClient _eshopClient;
    private readonly IErpPriceWriter _erpWriter;
    private readonly IProductPriceErpClient _erpReader;
    private readonly IProductVatRateProvider _vatRateProvider;
    private readonly IProductPriceChangeLogRepository _changeLog;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<SetProductPriceHandler> _logger;

    public SetProductPriceHandler(
        IEshopPriceListClient eshopClient,
        IErpPriceWriter erpWriter,
        IProductPriceErpClient erpReader,
        IProductVatRateProvider vatRateProvider,
        IProductPriceChangeLogRepository changeLog,
        ICurrentUserService currentUserService,
        ILogger<SetProductPriceHandler> logger)
    {
        _eshopClient = eshopClient;
        _erpWriter = erpWriter;
        _erpReader = erpReader;
        _vatRateProvider = vatRateProvider;
        _changeLog = changeLog;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<SetProductPriceResponse> Handle(
        SetProductPriceRequest request, CancellationToken cancellationToken)
    {
        var code = request.ProductCode;

        // 1. Shoptet is authoritative: a product with no row in the retail list cannot be priced.
        var oldPrice = await _eshopClient.GetPriceWithVatAsync(code, cancellationToken);
        if (oldPrice is null)
        {
            return await FailAsync(request, null, false, false,
                ErrorCodes.ProductPriceNotFoundInShoptet, $"{code} is not in the Shoptet retail price list.",
                cancellationToken);
        }

        // 2. Pre-flight the Flexi leg while nothing has been written yet.
        var erpItemId = await ResolveErpItemIdAsync(code, cancellationToken);
        var vatRate = await ResolveVatRateAsync(code, cancellationToken);
        if (erpItemId is null || vatRate is null)
        {
            return await FailAsync(request, oldPrice, false, false,
                ErrorCodes.ProductPriceFlexiItemIdUnknown,
                $"No Flexi ceník id or VAT rate for {code}; nothing was written.",
                cancellationToken);
        }

        var priceWithoutVat = Math.Round(
            request.PriceWithVat / (1 + vatRate.Value / 100m), PriceDecimals, MidpointRounding.AwayFromZero);

        // 3. Write Shoptet.
        try
        {
            await _eshopClient.SetPriceWithVatAsync(code, request.PriceWithVat, cancellationToken);
        }
        catch (Exception ex)
        {
            return await FailAsync(request, oldPrice, false, false,
                ErrorCodes.ProductPriceShoptetWriteFailed, ex.Message, cancellationToken);
        }

        // 4. Write Flexi. A failure here leaves the two systems divergent by design (D2):
        //    no rollback, no retry queue — the comparison screen is the safety net.
        try
        {
            await _erpWriter.SetPriceWithoutVatAsync(erpItemId.Value, priceWithoutVat, cancellationToken);
        }
        catch (Exception ex)
        {
            return await FailAsync(request, oldPrice, true, false,
                ErrorCodes.ProductPriceFlexiWriteFailed, ex.Message, cancellationToken);
        }

        await AppendAsync(request, oldPrice, true, true, null, cancellationToken);
        return new SetProductPriceResponse { PriceWithVat = request.PriceWithVat };
    }

    private async Task<int?> ResolveErpItemIdAsync(string code, CancellationToken ct)
    {
        var erpPrices = await _erpReader.GetAllAsync(forceReload: false, ct);
        var match = erpPrices.FirstOrDefault(p =>
            string.Equals(p.ProductCode, code, StringComparison.OrdinalIgnoreCase));

        return match is { ErpItemId: > 0 } ? match.ErpItemId : null;
    }

    private async Task<decimal?> ResolveVatRateAsync(string code, CancellationToken ct)
    {
        var rates = await _vatRateProvider.GetVatRatesAsync(ct);
        return rates.TryGetValue(code, out var rate) && rate >= 0 ? rate : null;
    }

    private async Task<SetProductPriceResponse> FailAsync(
        SetProductPriceRequest request, decimal? oldPrice, bool shoptetOk, bool flexiOk,
        ErrorCodes errorCode, string error, CancellationToken ct)
    {
        await AppendAsync(request, oldPrice, shoptetOk, flexiOk, error, ct);

        return new SetProductPriceResponse(
            errorCode, new Dictionary<string, string> { ["ProductCode"] = request.ProductCode });
    }

    /// <summary>
    /// The log is history, never read to decide anything, so it must never be able to change
    /// the outcome of the operation it records. Both remote writes have already landed by the
    /// time this runs on the success path — letting a failed INSERT surface would report a
    /// completed price change as an error and invite a pointless retry. On the failure paths
    /// it would mask the real error code. So: log the logging failure, and carry on.
    /// </summary>
    private async Task AppendAsync(
        SetProductPriceRequest request, decimal? oldPrice, bool shoptetOk, bool flexiOk,
        string? error, CancellationToken ct)
    {
        try
        {
            await _changeLog.AppendAsync(new ProductPriceChangeLog
            {
                ProductCode = request.ProductCode,
                OldPriceWithVat = oldPrice,
                NewPriceWithVat = request.PriceWithVat,
                ChangedAt = DateTime.UtcNow,
                ChangedBy = _currentUserService.GetCurrentUser().Email,
                ShoptetSucceeded = shoptetOk,
                FlexiSucceeded = flexiOk,
                ErrorMessage = error,
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Failed to append the price change log row for {ProductCode} " +
                    "(shoptet={ShoptetOk}, flexi={FlexiOk}); the price write itself is unaffected.",
                request.ProductCode, shoptetOk, flexiOk);
        }
    }
}
