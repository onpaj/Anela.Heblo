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
/// The Flexi pre-flight runs BEFORE the Shoptet write on purpose: a missing ceník id, an
/// unsupported price type, or a missing VAT rate is knowable up front and guarantees the
/// Flexi leg cannot succeed, so discovering it afterwards would manufacture an avoidable
/// divergence. The only partial failure this can produce is a genuine Flexi write error,
/// which is not knowable in advance.
/// </summary>
public class SetProductPriceHandler : IRequestHandler<SetProductPriceRequest, SetProductPriceResponse>
{
    private const int PriceDecimals = 2;

    /// <summary>
    /// The one Flexi price type whose <c>cenaZakl</c> holds a price EXCLUDING VAT, and so the
    /// only one needing conversion. <c>cenaZakl</c>'s VAT meaning is the item's own (see
    /// <c>FlexiProductPriceErpClient.MapToProductPrices</c> /
    /// <c>ProductPriceFlexiDto.IsPriceIncludingVat</c>) — for "sDph" it already IS the with-VAT
    /// price, so the operator's number is stored untouched.
    /// </summary>
    private const string ExcludingVatPriceType = "bezDph";

    /// <summary>
    /// True only for an item Flexi stores excluding VAT. Everything else — "sDph", and the
    /// null the ERP read returns when query 41 omits <c>typCenyDphK</c> — is treated as
    /// already storing the with-VAT price, so the operator's number is written through
    /// unchanged. Confirmed with the product owner: the operator always enters with VAT.
    /// </summary>
    private static bool IsStoredExcludingVat(string? erpPriceType) =>
        string.Equals(erpPriceType, ExcludingVatPriceType, StringComparison.OrdinalIgnoreCase);

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

        // 1. Shoptet is authoritative: a product with no row in the retail list cannot be
        //    priced. A read failure is distinct from a genuine absence, but both leave nothing
        //    written, so both are reported the same way to the operator.
        decimal? oldPrice;
        try
        {
            oldPrice = await _eshopClient.GetPriceWithVatAsync(code, cancellationToken);
        }
        catch (Exception ex)
        {
            return await FailAsync(request, null, false, false,
                ErrorCodes.ProductPriceShoptetWriteFailed, ex.Message);
        }

        if (oldPrice is null)
        {
            return await FailAsync(request, null, false, false,
                ErrorCodes.ProductPriceNotFoundInShoptet, $"{code} is not in the Shoptet retail price list.");
        }

        // 2. Pre-flight the Flexi leg while nothing has been written yet.
        //    A throwing read is a Flexi outage/timeout/500, not a fact about this product —
        //    it gets its own code so an operator is not told to go looking in Flexi for a
        //    ceník item that is actually there. ProductPriceFlexiItemIdUnknown stays for the
        //    genuine "the read resolved fine, this product simply has no id" case below.
        ProductPriceErp? erpMatch;
        try
        {
            erpMatch = await ResolveErpMatchAsync(code, cancellationToken);
        }
        catch (Exception ex)
        {
            return await FailAsync(request, oldPrice, false, false,
                ErrorCodes.ProductPriceErpReadFailed, ex.Message);
        }

        if (erpMatch is not { ErpItemId: > 0 })
        {
            return await FailAsync(request, oldPrice, false, false,
                ErrorCodes.ProductPriceFlexiItemIdUnknown,
                $"No Flexi ceník id for {code}; nothing was written.");
        }

        // The operator always enters a price INCLUDING VAT. What has to land in cenaZakl
        // depends on the item's own price type, mirroring how the read path interprets it
        // (FlexiProductPriceErpClient, IsPriceIncludingVat):
        //
        //   bezDph          -> cenaZakl is the excl-VAT price, so convert using the VAT rate.
        //   sDph / anything -> cenaZakl already IS the with-VAT price, so store it untouched.
        //
        // Only the bezDph branch needs a VAT rate at all, so an unrecognised VAT band must not
        // block the others.
        decimal basePrice;
        if (IsStoredExcludingVat(erpMatch.ErpPriceType))
        {
            decimal? vatRate;
            try
            {
                vatRate = await ResolveVatRateAsync(code, cancellationToken);
            }
            catch (Exception ex)
            {
                return await FailAsync(request, oldPrice, false, false,
                    ErrorCodes.ProductPriceErpReadFailed, ex.Message);
            }

            // A missing rate means the ERP's own VAT band was not one the adapter recognises
            // (see ProductPriceFlexiDto.VatRatesByLevel). Assuming a rate here would compute
            // the wrong cenaZakl, under-price the item in the live ERP, and stay invisible to
            // the comparison screen — which would reconstruct the same wrong number from the
            // same wrong rate and classify the row InAgreement. Its own code, because "no
            // ceník item" would send the operator hunting for something that is there.
            if (vatRate is null)
            {
                return await FailAsync(request, oldPrice, false, false,
                    ErrorCodes.ProductPriceFlexiVatRateUnknown,
                    $"No recognised Flexi VAT band for {code}; nothing was written.");
            }

            basePrice = Math.Round(
                request.PriceWithVat / (1 + vatRate.Value / 100m), PriceDecimals, MidpointRounding.AwayFromZero);
        }
        else
        {
            basePrice = request.PriceWithVat;
        }

        // 3. Write Shoptet.
        try
        {
            await _eshopClient.SetPriceWithVatAsync(code, request.PriceWithVat, cancellationToken);
        }
        catch (Exception ex)
        {
            return await FailAsync(request, oldPrice, false, false,
                ErrorCodes.ProductPriceShoptetWriteFailed, ex.Message);
        }

        // 4. Write Flexi. A failure here leaves the two systems divergent by design (D2):
        //    no rollback, no retry queue — the comparison screen is the safety net.
        try
        {
            await _erpWriter.SetBasePriceAsync(erpMatch.ErpItemId, basePrice, cancellationToken);
        }
        catch (Exception ex)
        {
            return await FailAsync(request, oldPrice, true, false,
                ErrorCodes.ProductPriceFlexiWriteFailed, ex.Message);
        }

        await AppendAsync(request, oldPrice, true, true, null);
        return new SetProductPriceResponse { PriceWithVat = request.PriceWithVat };
    }

    private async Task<ProductPriceErp?> ResolveErpMatchAsync(string code, CancellationToken ct)
    {
        var erpPrices = await _erpReader.GetAllAsync(forceReload: false, ct);
        return erpPrices.FirstOrDefault(p =>
            string.Equals(p.ProductCode, code, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<decimal?> ResolveVatRateAsync(string code, CancellationToken ct)
    {
        var rates = await _vatRateProvider.GetVatRatesAsync(ct);
        return rates.TryGetValue(code, out var rate) && rate >= 0 ? rate : null;
    }

    private async Task<SetProductPriceResponse> FailAsync(
        SetProductPriceRequest request, decimal? oldPrice, bool shoptetOk, bool flexiOk,
        ErrorCodes errorCode, string error)
    {
        await AppendAsync(request, oldPrice, shoptetOk, flexiOk, error);

        return new SetProductPriceResponse(
            errorCode, new Dictionary<string, string> { ["ProductCode"] = request.ProductCode });
    }

    /// <summary>
    /// The log is history, never read to decide anything, so it must never be able to change
    /// the outcome of the operation it records. Both remote writes have already landed by the
    /// time this runs on the success path — letting a failed INSERT surface would report a
    /// completed price change as an error and invite a pointless retry. On the failure paths
    /// it would mask the real error code. So: log the logging failure, and carry on.
    ///
    /// Always appends with <see cref="CancellationToken.None"/>, never the request's token:
    /// once step 3 (the Shoptet write) has landed, this row is the only record of a possible
    /// partial-failure state, and it must not be lost to the same cancellation that aborted
    /// the Flexi call or the caller's own request.
    /// </summary>
    private async Task AppendAsync(
        SetProductPriceRequest request, decimal? oldPrice, bool shoptetOk, bool flexiOk, string? error)
    {
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();

            await _changeLog.AppendAsync(new ProductPriceChangeLog
            {
                ProductCode = request.ProductCode,
                OldPriceWithVat = oldPrice,
                NewPriceWithVat = request.PriceWithVat,
                ChangedAt = DateTime.UtcNow,
                // Entra access tokens can omit an email claim; fall back rather than write a
                // null into an IsRequired() column, which would fail the insert and, being
                // swallowed below, silently drop the audit row for a live price change.
                ChangedBy = currentUser.Email ?? currentUser.Name ?? currentUser.Id ?? "unknown",
                ShoptetSucceeded = shoptetOk,
                FlexiSucceeded = flexiOk,
                ErrorMessage = error,
            }, CancellationToken.None);
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
