using Anela.Heblo.Application.Features.FinancialOverview.Model;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public class GetFinancialOverviewHandler : IRequestHandler<GetFinancialOverviewRequest, GetFinancialOverviewResponse>
{
    private readonly IFinancialAnalysisService _financialAnalysisService;
    private readonly ILogger<GetFinancialOverviewHandler> _logger;

    public GetFinancialOverviewHandler(
        IFinancialAnalysisService financialAnalysisService,
        ILogger<GetFinancialOverviewHandler> logger)
    {
        _financialAnalysisService = financialAnalysisService;
        _logger = logger;
    }

    public async Task<GetFinancialOverviewResponse> Handle(GetFinancialOverviewRequest request, CancellationToken cancellationToken)
    {
        var months = request.Months ?? 6;

        _logger.LogInformation("Handling financial overview request for {Months} months, IncludeStock={IncludeStock}",
            months, request.IncludeStockData);

        return await _financialAnalysisService.GetFinancialOverviewAsync(
            months,
            request.IncludeStockData,
            cancellationToken);
    }

}