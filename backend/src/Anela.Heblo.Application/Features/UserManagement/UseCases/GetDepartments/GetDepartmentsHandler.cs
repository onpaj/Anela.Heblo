using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.UserManagement.UseCases.GetDepartments;

public class GetDepartmentsHandler : IRequestHandler<GetDepartmentsRequest, GetDepartmentsResponse>
{
    private readonly IDepartmentQueryService _departmentQueryService;
    private readonly ILogger<GetDepartmentsHandler> _logger;

    public GetDepartmentsHandler(IDepartmentQueryService departmentQueryService, ILogger<GetDepartmentsHandler> logger)
    {
        _departmentQueryService = departmentQueryService;
        _logger = logger;
    }

    public async Task<GetDepartmentsResponse> Handle(GetDepartmentsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var departments = await _departmentQueryService.GetDepartmentsAsync(cancellationToken);

            return new GetDepartmentsResponse
            {
                Success = true,
                Departments = departments
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle GetDepartments");

            return new GetDepartmentsResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InternalServerError,
                Departments = new List<DepartmentDto>()
            };
        }
    }
}
