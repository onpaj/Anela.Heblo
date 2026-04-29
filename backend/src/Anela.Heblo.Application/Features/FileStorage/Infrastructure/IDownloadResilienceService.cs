namespace Anela.Heblo.Application.Features.FileStorage.Infrastructure;

public interface IDownloadResilienceService
{
    Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);
}
