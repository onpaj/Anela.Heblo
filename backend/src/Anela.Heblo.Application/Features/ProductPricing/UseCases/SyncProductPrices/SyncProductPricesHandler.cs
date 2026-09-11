using Anela.Heblo.Application.Features.ProductPricing.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SyncProductPrices;

/// <summary>
/// A Shoptet or Flexi read failure is left to propagate, exactly as
/// <c>GetPriceDivergenceReportHandler</c> leaves it: there is no partial state to report and
/// nothing was written, so an error code of its own would add a vocabulary without adding
/// information the operator can act on.
/// </summary>
public class SyncProductPricesHandler : IRequestHandler<SyncProductPricesRequest, SyncProductPricesResponse>
{
    private readonly IPriceComparisonService _comparisonService;

    public SyncProductPricesHandler(IPriceComparisonService comparisonService) =>
        _comparisonService = comparisonService;

    public async Task<SyncProductPricesResponse> Handle(
        SyncProductPricesRequest request, CancellationToken cancellationToken)
    {
        var report = await _comparisonService.BuildScopedReportAsync(request.ProductCodes, cancellationToken);

        return new SyncProductPricesResponse { Rows = report.Rows };
    }
}
