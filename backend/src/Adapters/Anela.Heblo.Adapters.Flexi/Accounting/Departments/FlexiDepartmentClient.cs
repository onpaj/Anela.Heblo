using Anela.Heblo.Domain.Features.InvoiceClassification;
using Microsoft.Extensions.Caching.Memory;
using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;

namespace Anela.Heblo.Adapters.Flexi.Accounting.Departments;

public class FlexiDepartmentClient : Domain.Features.InvoiceClassification.IDepartmentClient
{
    private readonly IDepartmentClient _client;
    private readonly IMemoryCache _cache;
    private const string DepartmentsCacheKey = "flexi_departments";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public FlexiDepartmentClient(IDepartmentClient client, IMemoryCache cache)
    {
        _client = client;
        _cache = cache;
    }

    public async Task<IEnumerable<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(DepartmentsCacheKey, out IEnumerable<Department>? cachedDepartments))
        {
            return cachedDepartments!;
        }

        var departments = await _client.GetAsync(cancellationToken);

        var result = departments.Select(s => new Department()
        {
            Id = s.Code,
            Name = s.Name,
        }).ToList();

        _cache.Set(DepartmentsCacheKey, result, CacheDuration);

        return result;
    }

    public async Task<Department?> GetDepartmentByIdAsync(string departmentId, CancellationToken cancellationToken = default)
    {
        var departments = await GetDepartmentsAsync(cancellationToken);
        return departments.FirstOrDefault(s => s.Id == departmentId);
    }
}