namespace Anela.Heblo.Application.Features.DataQuality.Services;

public interface IDriftDqtJobRunner
{
    Task RunAsync(Guid runId, CancellationToken ct = default);
}
