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
        var shouldRotate = await ShouldRotateAsync(request, ct);

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

        if (shouldRotate)
            stream = await RotateHalfTurnAsync(stream, request, ct);

        return new GetPackageLabelPdfResponse
        {
            Content = stream,
            ContentType = contentType,
            FileName = $"{request.OrderCode}-{request.PackageNumber}.pdf",
        };
    }

    /// <summary>
    /// Some carriers' labels come out of the Zebra the wrong way round, so each carrier has its own
    /// flag that turns its labels upside down. Personal pickup has no carrier label, so no flag.
    /// </summary>
    private static readonly IReadOnlyDictionary<Carriers, string> RotationFlagByCarrier =
        new Dictionary<Carriers, string>
        {
            [Carriers.GLS] = FeatureFlagKeys.GlsLabelRotation,
            [Carriers.PPL] = FeatureFlagKeys.PplLabelRotation,
            [Carriers.Zasilkovna] = FeatureFlagKeys.ZasilkovnaLabelRotation,
        };

    /// <summary>
    /// Rotates when the order's carrier has its rotation flag on. With every flag off the carrier
    /// is not looked up at all. A failed flag check or carrier lookup only skips the rotation —
    /// the label still prints.
    /// </summary>
    private async Task<bool> ShouldRotateAsync(GetPackageLabelPdfRequest request, CancellationToken ct)
    {
        try
        {
            var rotatedCarriers = await GetRotatedCarriersAsync(ct);
            if (rotatedCarriers.Count == 0)
                return false;

            var shippingGuid = await _orderShippingSource.GetShippingMethodGuidAsync(request.OrderCode, ct);
            var carrier = shippingGuid is null ? null : _shippingCatalog.ResolveCarrierByShippingGuid(shippingGuid);
            return carrier is not null && rotatedCarriers.Contains(carrier.Value);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Could not resolve carrier for order {OrderCode} package {PackageNumber}; printing label unrotated",
                request.OrderCode, request.PackageNumber);
            return false;
        }
    }

    private async Task<IReadOnlySet<Carriers>> GetRotatedCarriersAsync(CancellationToken ct)
    {
        var rotatedCarriers = new HashSet<Carriers>();
        foreach (var (carrier, flagKey) in RotationFlagByCarrier)
        {
            if (await _featureFlags.IsEnabledAsync(flagKey, ct))
                rotatedCarriers.Add(carrier);
        }

        return rotatedCarriers;
    }

    private async Task<Stream> RotateHalfTurnAsync(Stream stream, GetPackageLabelPdfRequest request, CancellationToken ct)
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
            return new MemoryStream(LabelPdfRotator.RotateHalfTurn(original));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to rotate label PDF for order {OrderCode} package {PackageNumber}; printing label unrotated",
                request.OrderCode, request.PackageNumber);
            return new MemoryStream(original);
        }
    }
}
