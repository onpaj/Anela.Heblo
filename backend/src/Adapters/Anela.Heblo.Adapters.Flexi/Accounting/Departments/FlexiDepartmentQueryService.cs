using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Adapters.Flexi.Accounting.Departments;

public class FlexiDepartmentQueryService : IDepartmentQueryService
{
    private readonly IDepartmentClient _departmentClient;

    public FlexiDepartmentQueryService(IDepartmentClient departmentClient)
    {
        _departmentClient = departmentClient;
    }

    public async Task<List<DepartmentDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        var departments = await _departmentClient.GetDepartmentsAsync(cancellationToken);

        return departments.Select(d => new DepartmentDto { Id = d.Id, Name = d.Name }).ToList();
    }
}
