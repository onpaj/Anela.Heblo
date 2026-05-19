using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;

public class GetShipmentLabelPdfHandler
    : IRequestHandler<GetShipmentLabelPdfRequest, GetShipmentLabelPdfResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GetShipmentLabelPdfHandler> _logger;

    public GetShipmentLabelPdfHandler(
        IShipmentClient shipmentClient,
        IHttpClientFactory httpClientFactory,
        ILogger<GetShipmentLabelPdfHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<GetShipmentLabelPdfResponse> Handle(
        GetShipmentLabelPdfRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var labels = await _shipmentClient.GetLabelsByOrderCodeAsync(
                request.OrderCode, cancellationToken);

            var package = labels.FirstOrDefault(l =>
                l.ShipmentGuid == request.ShipmentGuid &&
                l.PackageName == request.PackageName);

            if (package is null || package.LabelUrl is null)
            {
                return new GetShipmentLabelPdfResponse(ErrorCodes.ShipmentLabelPdfNotFound);
            }

            var httpClient = _httpClientFactory.CreateClient();
            var pdfResponse = await httpClient.GetAsync(package.LabelUrl, cancellationToken);

            if (!pdfResponse.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "PDF download failed for order {OrderCode} package {PackageName}: HTTP {StatusCode}",
                    request.OrderCode, request.PackageName, (int)pdfResponse.StatusCode);
                return new GetShipmentLabelPdfResponse(ErrorCodes.InternalServerError);
            }

            var ms = new MemoryStream();
            await pdfResponse.Content.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;
            return new GetShipmentLabelPdfResponse(ms);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get label PDF for order {OrderCode} package {PackageName}",
                request.OrderCode, request.PackageName);
            return new GetShipmentLabelPdfResponse(ErrorCodes.InternalServerError);
        }
    }
}
