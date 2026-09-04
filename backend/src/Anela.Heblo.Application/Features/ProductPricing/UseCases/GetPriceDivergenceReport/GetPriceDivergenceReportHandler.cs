using Anela.Heblo.Application.Features.ProductPricing.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.GetPriceDivergenceReport;

public class GetPriceDivergenceReportHandler
    : IRequestHandler<GetPriceDivergenceReportRequest, GetPriceDivergenceReportResponse>
{
    private readonly IPriceDivergenceReportService _reportService;

    public GetPriceDivergenceReportHandler(IPriceDivergenceReportService reportService)
    {
        _reportService = reportService;
    }

    public async Task<GetPriceDivergenceReportResponse> Handle(
        GetPriceDivergenceReportRequest request, CancellationToken cancellationToken)
    {
        var report = await _reportService.BuildReportAsync(cancellationToken);

        return new GetPriceDivergenceReportResponse
        {
            Rows = report.Rows,
            Summary = report.Summary,
        };
    }
}
