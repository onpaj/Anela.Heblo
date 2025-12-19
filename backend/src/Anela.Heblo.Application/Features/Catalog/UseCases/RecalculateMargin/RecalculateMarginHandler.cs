using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RecalculateMargin;

public class RecalculateMarginHandler : IRequestHandler<RecalculateMarginRequest, RecalculateMarginResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IMarginCalculationService _marginCalculationService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecalculateMarginHandler> _logger;

    public RecalculateMarginHandler(
        ICatalogRepository catalogRepository,
        IMarginCalculationService marginCalculationService,
        TimeProvider timeProvider,
        ILogger<RecalculateMarginHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _marginCalculationService = marginCalculationService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<RecalculateMarginResponse> Handle(RecalculateMarginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // Get catalog item
            var catalogItem = await _catalogRepository.SingleOrDefaultAsync(
                x => x.ProductCode == request.ProductCode,
                cancellationToken);

            if (catalogItem == null)
            {
                return new RecalculateMarginResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ProductNotFound,
                    Params = new Dictionary<string, string> { { "productCode", request.ProductCode } }
                };
            }

            // Calculate date range - last N months from today
            var currentDate = _timeProvider.GetUtcNow().Date;
            var dateFrom = DateOnly.FromDateTime(currentDate.AddMonths(-request.MonthsBack));
            var dateTo = DateOnly.FromDateTime(currentDate);

            _logger.LogInformation(
                "Recalculating margins for product {ProductCode} from {DateFrom} to {DateTo}",
                request.ProductCode,
                dateFrom,
                dateTo);

            // Recalculate margins using the service
            var marginHistory = await _marginCalculationService.GetMarginAsync(
                catalogItem,
                dateFrom,
                dateTo,
                cancellationToken);

            // Convert to DTOs using the same pattern as GetCatalogDetailHandler
            var marginHistoryDtos = ConvertToMarginHistoryDtos(marginHistory);

            _logger.LogInformation(
                "Successfully recalculated {Count} months of margin data for product {ProductCode}",
                marginHistoryDtos.Count,
                request.ProductCode);

            return new RecalculateMarginResponse
            {
                Success = true,
                MarginHistory = marginHistoryDtos
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recalculating margins for product {ProductCode}", request.ProductCode);

            return new RecalculateMarginResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InternalServerError,
                Params = new Dictionary<string, string>
                {
                    { "productCode", request.ProductCode },
                    { "error", ex.Message }
                }
            };
        }
    }

    private List<MarginHistoryDto> ConvertToMarginHistoryDtos(MonthlyMarginHistory marginHistory)
    {
        return marginHistory.MonthlyData
            .OrderByDescending(m => m.Month)
            .Select(m => new MarginHistoryDto
            {
                Date = m.Month,
                SellingPrice = m.M3.CostTotal + m.M3.Amount, // Reconstructed selling price from highest level
                TotalCost = m.M0.CostTotal, // Base cost (material + manufacturing)

                // M0 - Material + Manufacturing costs
                M0 = new MarginLevelDto
                {
                    Percentage = m.M0.Percentage,
                    Amount = m.M0.Amount,
                    CostLevel = m.M0.CostLevel,
                    CostTotal = m.M0.CostTotal
                },

                // M1 - M0 + Manufacturing costs (if different)
                M1 = new MarginLevelDto
                {
                    Percentage = m.M1.Percentage,
                    Amount = m.M1.Amount,
                    CostLevel = m.M1.CostLevel,
                    CostTotal = m.M1.CostTotal
                },

                // M2 - M1 + Sales costs
                M2 = new MarginLevelDto
                {
                    Percentage = m.M2.Percentage,
                    Amount = m.M2.Amount,
                    CostLevel = m.M2.CostLevel,
                    CostTotal = m.M2.CostTotal
                },

                // M3 - M2 + Overhead costs (final margin)
                M3 = new MarginLevelDto
                {
                    Percentage = m.M3.Percentage,
                    Amount = m.M3.Amount,
                    CostLevel = m.M3.CostLevel,
                    CostTotal = m.M3.CostTotal
                }
            }).ToList();
    }
}
