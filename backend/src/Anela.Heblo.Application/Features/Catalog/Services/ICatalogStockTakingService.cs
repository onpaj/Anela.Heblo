using Anela.Heblo.Application.Features.Catalog.Contracts;
using System.ComponentModel;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public interface ICatalogStockTakingService
{
    Task<string> EnqueueStockTakingAsync(
        string productCode,
        decimal targetAmount,
        bool softStockTaking = false,
        CancellationToken cancellationToken = default);

    [DisplayName("StockTaking-{0}-{1}")]
    Task<StockTakingResultDto> ProcessStockTakingAsync(
        string productCode,
        decimal targetAmount,
        bool softStockTaking = false,
        CancellationToken cancellationToken = default);
}