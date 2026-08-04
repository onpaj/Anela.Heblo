using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.UserManagement.UseCases.GetDepartments;

public class GetDepartmentsResponse : BaseResponse
{
    public List<DepartmentDto> Departments { get; set; } = new();
}
