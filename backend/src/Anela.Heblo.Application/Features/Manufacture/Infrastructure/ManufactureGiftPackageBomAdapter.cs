using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Infrastructure;

internal sealed class ManufactureGiftPackageBomAdapter : IGiftPackageBomSource
{
    private readonly IManufactureClient _client;

    public ManufactureGiftPackageBomAdapter(IManufactureClient client)
    {
        _client = client;
    }

    public async Task<List<LogisticsGiftPackagePart>> GetSetPartsAsync(string setProductCode, CancellationToken cancellationToken = default)
    {
        var parts = await _client.GetSetPartsAsync(setProductCode, cancellationToken);
        return parts
            .Select(p => new LogisticsGiftPackagePart
            {
                ProductCode = p.ProductCode,
                ProductName = p.ProductName,
                Amount = p.Amount,
            })
            .ToList();
    }
}
