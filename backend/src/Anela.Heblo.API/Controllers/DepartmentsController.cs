using Microsoft.AspNetCore.Mvc;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DepartmentsController : ControllerBase
{
    private readonly IDepartmentClient _departmentClient;

    public DepartmentsController(IDepartmentClient departmentClient)
    {
        _departmentClient = departmentClient;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Department>>> GetDepartments(CancellationToken cancellationToken = default)
    {
        var departments = await _departmentClient.GetDepartmentsAsync(cancellationToken);
        return Ok(departments);
    }
}