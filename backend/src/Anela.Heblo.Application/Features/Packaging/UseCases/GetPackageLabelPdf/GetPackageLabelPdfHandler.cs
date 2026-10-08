using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.Packaging.Contracts;
using Anela.Heblo.Application.Features.Packaging.Services;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;

public class GetPackageLabelPdfHandler : IRequestHandler<GetPackageLabelPdfRequest, GetPackageLabelPdfResponse>
{
    public const string HttpClientName = "ShipmentLabelDownloader";

    /// <summary>
    /// How far a flagged carrier's label content is moved to the left (printing starts sooner),
    /// so the label fits the shorter label stock on the Zebra. 10 mm in PDF points (1/72 in).
    /// </summary>
    private const double LabelLeftOffsetPoints = 10 / 25.4 * 72;

    private readonly IShipmentClient _shipmentClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPackingOrderShippingSource _orderShippingSource;
    private readonly IShippingMethodCatalog _shippingCatalog;
    private readonly IFeatureFlagChecker _featureFlags;
    private readonly ILogger<GetPackageLabelPdfHandler> _logger;

    public GetPackageLabelPdfHandler(
        IShipmentClient shipmentClient,
        IHttpClientFactory httpClientFactory,
        IPackingOrderShippingSource orderShippingSource,
        IShippingMethodCatalog shippingCatalog,
        IFeatureFlagChecker featureFlags,
        ILogger<GetPackageLabelPdfHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _httpClientFactory = httpClientFactory;
        _orderShippingSource = orderShippingSource;
        _shippingCatalog = shippingCatalog;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    public async Task<GetPackageLabelPdfResponse> Handle(GetPackageLabelPdfRequest request, CancellationToken ct)
    {
        // Carrier package names are not unique per package (custom-packaging shipments report
        // the same name for every package), so the package is resolved by its 1-based position
        // in the order's labels — matching the PackageNumber persisted at scan time.
        var index = request.PackageNumber - 1;

        // The frontend polls this endpoint until the label is ready, so 404s are expected and
        // frequent during that window. Poll requests are logged at Debug to avoid flooding the
        // logs; the final (post-timeout) confirmation request is not a poll, so its 404 is logged
        // at Warning with full diagnostics.
        var notReadyLevel = request.IsPoll ? LogLevel.Debug : LogLevel.Warning;

        var labels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        var label = labels.ElementAtOrDefault(index);

        if (label is null)
        {
            _logger.Log(notReadyLevel,
                "Label not found for order {OrderCode} package {PackageNumber} (index {Index}): " +
                "live shipment fetch returned {LabelCount} label(s) [{PackageNames}]",
                request.OrderCode, request.PackageNumber, index, labels.Count,
                string.Join(", ", labels.Select(l => $"{l.PackageName}(url={(string.IsNullOrWhiteSpace(l.LabelUrl) ? "none" : "yes")})")));
            return new GetPackageLabelPdfResponse(ErrorCodes.PackageLabelNotFound);
        }

        if (string.IsNullOrWhiteSpace(label.LabelUrl))
        {
            _logger.Log(notReadyLevel,
                "Label URL unavailable for order {OrderCode} package {PackageNumber} (single-shot check, LabelUrl empty)",
                request.OrderCode, request.PackageNumber);
            return new GetPackageLabelPdfResponse(ErrorCodes.PackageLabelNotFound);
        }

        // Resolved before the download so a cancelled lookup never leaves a carrier response open.
        var shouldShift = await ShouldShiftAsync(request, ct);

        var http = _httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage carrierResponse;
        try
        {
            carrierResponse = await http.GetAsync(label.LabelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download label PDF for order {OrderCode} package {PackageNumber} from {LabelUrl}",
                request.OrderCode, request.PackageNumber, label.LabelUrl);
            return new GetPackageLabelPdfResponse(ErrorCodes.PackageLabelDownloadFailed);
        }

        if (!carrierResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Carrier returned {StatusCode} for label PDF (order {OrderCode}, package {PackageNumber})",
                (int)carrierResponse.StatusCode, request.OrderCode, request.PackageNumber);
            carrierResponse.Dispose();
            return new GetPackageLabelPdfResponse(ErrorCodes.PackageLabelDownloadFailed);
        }

        var contentType = carrierResponse.Content.Headers.ContentType?.MediaType ?? "application/pdf";
        var stream = await carrierResponse.Content.ReadAsStreamAsync(ct);

        if (shouldShift)
            stream = await ShiftLeftAsync(stream, request, ct);

        return new GetPackageLabelPdfResponse
        {
            Content = stream,
            ContentType = contentType,
            FileName = $"{request.OrderCode}-{request.PackageNumber}.pdf",
        };
    }

    /// <summary>
    /// Carrier labels are longer than the Zebra's label stock, so each carrier has its own flag that
    /// moves its labels to the left. Personal pickup has no carrier label, so no flag.
    /// </summary>
    private static readonly IReadOnlyDictionary<Carriers, string> OffsetFlagByCarrier =
        new Dictionary<Carriers, string>
        {
            [Carriers.GLS] = FeatureFlagKeys.GlsLabelOffset,
            [Carriers.PPL] = FeatureFlagKeys.PplLabelOffset,
            [Carriers.Zasilkovna] = FeatureFlagKeys.ZasilkovnaLabelOffset,
        };

    /// <summary>
    /// Shifts when the order's carrier has its offset flag on. With every flag off the carrier
    /// is not looked up at all. A failed flag check or carrier lookup only skips the shift —
    /// the label still prints.
    /// </summary>
    private async Task<bool> ShouldShiftAsync(GetPackageLabelPdfRequest request, CancellationToken ct)
    {
        try
        {
            var shiftedCarriers = await GetShiftedCarriersAsync(ct);
            if (shiftedCarriers.Count == 0)
                return false;

            var shippingGuid = await _orderShippingSource.GetShippingMethodGuidAsync(request.OrderCode, ct);
            var carrier = shippingGuid is null ? null : _shippingCatalog.ResolveCarrierByShippingGuid(shippingGuid);
            return carrier is not null && shiftedCarriers.Contains(carrier.Value);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Could not resolve carrier for order {OrderCode} package {PackageNumber}; printing label without offset",
                request.OrderCode, request.PackageNumber);
            return false;
        }
    }

    private async Task<IReadOnlySet<Carriers>> GetShiftedCarriersAsync(CancellationToken ct)
    {
        var shiftedCarriers = new HashSet<Carriers>();
        foreach (var (carrier, flagKey) in OffsetFlagByCarrier)
        {
            if (await _featureFlags.IsEnabledAsync(flagKey, ct))
                shiftedCarriers.Add(carrier);
        }

        return shiftedCarriers;
    }

    private async Task<Stream> ShiftLeftAsync(Stream stream, GetPackageLabelPdfRequest request, CancellationToken ct)
    {
        byte[] original;
        await using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            original = buffer.ToArray();
        }

        try
        {
            return new MemoryStream(LabelPdfShifter.ShiftLeft(original, LabelLeftOffsetPoints));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to shift label PDF for order {OrderCode} package {PackageNumber}; printing label without offset",
                request.OrderCode, request.PackageNumber);
            return new MemoryStream(original);
        }
    }
}
