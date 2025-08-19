namespace Anela.Heblo.Domain.Features.Manufacture;

public interface IManufactureHistoryClient
{
    Task<List<ManufactureHistoryRecord>> GetHistoryAsync(DateTime dateFrom, DateTime dateTo, string? productCode = null, CancellationToken cancellationToken = default);
}