using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public interface IDriftDqtComparer
{
    DqtTestType TestType { get; }
    Task<DriftComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
