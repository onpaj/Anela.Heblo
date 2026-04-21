using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.DataQuality;

public interface IDqtRunRepository : IRepository<DqtRun, Guid>
{
    Task<DqtRun?> GetLatestByTestTypeAsync(DqtTestType testType, CancellationToken cancellationToken = default);
    Task<(List<DqtRun> Items, int TotalCount)> GetPaginatedAsync(
        DqtTestType? testType,
        DqtRunStatus? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<DqtRun?> GetWithResultsAsync(Guid id, int resultPage, int resultPageSize, CancellationToken cancellationToken = default);
}
