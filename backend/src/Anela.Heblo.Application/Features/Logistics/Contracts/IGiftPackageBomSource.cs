using Anela.Heblo.Application.Features.Logistics.Contracts.Models;

namespace Anela.Heblo.Application.Features.Logistics.Contracts;

/// <summary>
/// Logistics-owned abstraction over the bill of materials of a gift package set.
/// Implemented by the Manufacture module via an adapter.
/// </summary>
public interface IGiftPackageBomSource
{
    Task<List<LogisticsGiftPackagePart>> GetSetPartsAsync(string setProductCode, CancellationToken cancellationToken = default);
}
