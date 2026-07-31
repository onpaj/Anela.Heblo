using Anela.Heblo.Application.Features.UserManagement.Contracts;

namespace Anela.Heblo.Application.Features.UserManagement.Services;

public interface IDepartmentQueryService
{
    Task<List<DepartmentDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
}
