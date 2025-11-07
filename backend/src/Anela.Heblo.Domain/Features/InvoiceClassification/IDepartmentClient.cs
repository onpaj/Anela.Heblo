namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IDepartmentClient
{
    Task<IEnumerable<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<Department?> GetDepartmentByIdAsync(string departmentId, CancellationToken cancellationToken = default);
}

public class FixedDepartmentClient : IDepartmentClient
{
    private static readonly Department[] Departments = 
    {
        new() { Id = "VYROBA", Name = "Výroba" },
        new() { Id = "SKLAD", Name = "Sklad" },
        new() { Id = "MARKETING", Name = "Marketing" },
        new() { Id = "CENTRALA", Name = "Centrála" },
        new() { Id = "BUVOL", Name = "Bůvol" }
    };

    public Task<IEnumerable<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<Department>>(Departments);
    }

    public Task<Department?> GetDepartmentByIdAsync(string departmentId, CancellationToken cancellationToken = default)
    {
        var department = Departments.FirstOrDefault(d => d.Id.Equals(departmentId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(department);
    }
}

public class Department
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}