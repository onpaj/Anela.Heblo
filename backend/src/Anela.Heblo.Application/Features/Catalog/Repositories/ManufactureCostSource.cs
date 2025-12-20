using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Repositories;

public class ManufactureCostSource : IFlatManufactureCostSource
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ManufactureCostSource> _logger;

    public ManufactureCostSource(
        TimeProvider timeProvider,
        ILogger<ManufactureCostSource> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }


    public async Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(List<string>? productCodes = null, DateOnly? dateFrom = null, DateOnly? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        
    }
}