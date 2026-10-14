using Anela.Heblo.Application.Features.Packaging.Contracts;
using Anela.Heblo.Application.Features.Packaging.Services;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Packaging;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;

public class GetPackageLabelPdfHandler : IRequestHandler<GetPackageLabelPdfRequest, GetPackageLabelPdfResponse>
{
    public const string HttpClientName = "ShipmentLabelDownloader";

    /// <summary>PDF points (1/72 in) per millimetre.</summary>
    private const double PointsPerMm = 72 / 25.4;

    private readonly IShipmentClient _shipmentClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPackingOrderShippingSource _orderShippingSource;
    private readonly IShippingMethodCatalog _shippingCatalog;
    private readonly ICarrierLabelOffsetRepository _labelOffsets;
    private readonly ILogger<GetPackageLabelPdfHandler> _logger;

    public GetPackageLabelPdfHandler(
        IShipmentClient shipmentClient,
        IHttpClientFactory httpClientFactory,
        IPackingOrderShippingSource orderShippingSource,
        IShippingMethodCatalog shippingCatalog,
        ICarrierLabelOffsetRepository labelOffsets,
        ILogger<GetPackageLabelPdfHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _httpClientFactory = httpClientFactory;
        _orderShippingSource = orderShippingSource;
        _shippingCatalog = shippingCatalog;
        _labelOffsets = labelOffsets;
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
        var offset = await GetOffsetAsync(request, ct);

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

        if (offset != default)
            stream = await ShiftAsync(stream, offset, request, ct);

        return new GetPackageLabelPdfResponse
        {
            Content = stream,
            ContentType = contentType,
            FileName = $"{request.OrderCode}-{request.PackageNumber}.pdf",
        };
    }

    /// <summary>
    /// Looks up the per-carrier offset saved in Nastavení expedice and converts it to PDF points.
    /// When no carrier has a non-zero offset the order's carrier is not looked up at all. A failed
    /// offset read or carrier lookup only skips the offset — the label still prints.
    /// </summary>
    private async Task<(double Right, double Up)> GetOffsetAsync(GetPackageLabelPdfRequest request, CancellationToken ct)
    {
        try
        {
            var offsets = await _labelOffsets.GetAllAsync(ct);
            if (!offsets.Any(o => o.OffsetXMm != 0 || o.OffsetYMm != 0))
                return default;

            var carrier = await ResolveCarrierAsync(request.OrderCode, ct);
            var offset = offsets.FirstOrDefault(o => o.Carrier == carrier);
            _logger.LogDebug(
                "Label offset for order {OrderCode}: carrier {Carrier}, X {OffsetXMm} mm, Y {OffsetYMm} mm",
                request.OrderCode, carrier?.ToString() ?? "unresolved", offset?.OffsetXMm ?? 0, offset?.OffsetYMm ?? 0);

            return offset is null ? default : (offset.OffsetXMm * PointsPerMm, offset.OffsetYMm * PointsPerMm);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Could not resolve label offset for order {OrderCode} package {PackageNumber}; printing label without offset",
                request.OrderCode, request.PackageNumber);
            return default;
        }
    }

    private async Task<Carriers?> ResolveCarrierAsync(string orderCode, CancellationToken ct)
    {
        var shippingGuid = await _orderShippingSource.GetShippingMethodGuidAsync(orderCode, ct);
        return shippingGuid is null ? null : _shippingCatalog.ResolveCarrierByShippingGuid(shippingGuid);
    }

    private async Task<Stream> ShiftAsync(
        Stream stream, (double Right, double Up) offset, GetPackageLabelPdfRequest request, CancellationToken ct)
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
            return new MemoryStream(LabelPdfShifter.Shift(original, offset.Right, offset.Up));
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
