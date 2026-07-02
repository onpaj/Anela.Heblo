using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public interface IDqtJobRunner
{
    bool CanHandle(DqtTestType testType);
    Task RunAsync(Guid runId, CancellationToken ct = default);
}
