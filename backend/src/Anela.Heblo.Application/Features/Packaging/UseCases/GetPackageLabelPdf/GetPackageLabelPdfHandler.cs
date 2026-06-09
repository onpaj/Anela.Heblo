using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;

public class GetPackageLabelPdfHandler : IRequestHandler<GetPackageLabelPdfRequest, GetPackageLabelPdfResponse>
{
    public const string HttpClientName = "ShipmentLabelDownloader";

    private readonly IShipmentClient _shipmentClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GetPackageLabelPdfHandler> _logger;

    private const int LabelReadinessRetries = 5;
    private readonly TimeSpan _labelReadinessDelay;

    public GetPackageLabelPdfHandler(
        IShipmentClient shipmentClient,
        IHttpClientFactory httpClientFactory,
        ILogger<GetPackageLabelPdfHandler> logger,
        TimeSpan? labelReadinessDelay = null)
    {
        _shipmentClient = shipmentClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _labelReadinessDelay = labelReadinessDelay ?? TimeSpan.FromSeconds(1);
    }

    public async Task<GetPackageLabelPdfResponse> Handle(GetPackageLabelPdfRequest request, CancellationToken ct)
    {
        // Carrier package names are not unique per package (custom-packaging shipments report
        // the same name for every package), so the package is resolved by its 1-based position
        // in the order's labels — matching the PackageNumber persisted at scan time.
        var index = request.PackageNumber - 1;

        var labels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        var label = labels.ElementAtOrDefault(index);

        if (label is null)
            return new GetPackageLabelPdfResponse(ErrorCodes.PackageLabelNotFound);

        // Carrier label generation may be briefly async after shipment creation — poll until ready.
        for (var attempt = 0; attempt < LabelReadinessRetries && string.IsNullOrWhiteSpace(label.LabelUrl); attempt++)
        {
            await Task.Delay(_labelReadinessDelay, ct);
            labels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
            label = labels.ElementAtOrDefault(index);
            if (label is null) break;
        }

        if (label is null || string.IsNullOrWhiteSpace(label.LabelUrl))
        {
            return new GetPackageLabelPdfResponse(ErrorCodes.PackageLabelNotFound);
        }

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

        return new GetPackageLabelPdfResponse
        {
            Content = stream,
            ContentType = contentType,
            FileName = $"{request.OrderCode}-{request.PackageNumber}.pdf",
        };
    }
}
