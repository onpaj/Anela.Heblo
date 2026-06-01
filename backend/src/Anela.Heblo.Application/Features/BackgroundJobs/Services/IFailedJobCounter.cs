namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

public interface IFailedJobCounter
{
    Task<long> GetFailedCountAsync(CancellationToken cancellationToken = default);
}
