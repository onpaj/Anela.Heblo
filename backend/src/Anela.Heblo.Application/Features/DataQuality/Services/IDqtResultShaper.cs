using Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;
using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

/// <summary>
/// Populates the result portion (Results / DriftResults / TotalDriftResults) of a
/// GetDqtRunDetailResponse for a given DqtRun's TestType. Implementations must not set
/// Run, Success, or ErrorCode on the response — those remain owned by
/// GetDqtRunDetailHandler, which resolves the matching IDqtResultShaper via CanHandle
/// before calling ShapeAsync, exactly like IDqtJobRunner is resolved in RunDqtHandler.
/// </summary>
public interface IDqtResultShaper
{
    bool CanHandle(DqtTestType testType);

    Task ShapeAsync(
        DqtRun run,
        GetDqtRunDetailResponse response,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
