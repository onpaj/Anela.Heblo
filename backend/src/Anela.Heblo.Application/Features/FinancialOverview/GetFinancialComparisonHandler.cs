using Anela.Heblo.Application.Features.FinancialOverview.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public class GetFinancialComparisonHandler
    : IRequestHandler<GetFinancialComparisonRequest, GetFinancialComparisonResponse>
{
    private readonly IFinancialAnalysisService _financialAnalysisService;
    private readonly ILogger<GetFinancialComparisonHandler> _logger;

    public GetFinancialComparisonHandler(
        IFinancialAnalysisService financialAnalysisService,
        ILogger<GetFinancialComparisonHandler> logger)
    {
        _financialAnalysisService = financialAnalysisService;
        _logger = logger;
    }

    public async Task<GetFinancialComparisonResponse> Handle(
        GetFinancialComparisonRequest request, CancellationToken cancellationToken)
    {
        var years = request.Years ?? 3;

        _logger.LogInformation(
            "Handling financial comparison request for {Years} years, IncludeStock={Stock}, IncludePartial={Partial}",
            years, request.IncludeStockData, request.IncludePartialMonth);

        return await _financialAnalysisService.GetFinancialComparisonAsync(
            years,
            request.IncludeStockData,
            request.ExcludedDepartments,
            request.IncludePartialMonth,
            cancellationToken);
    }
}
