namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IDepartmentClient
{
    Task<IEnumerable<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<Department?> GetDepartmentByIdAsync(string departmentId, CancellationToken cancellationToken = default);
}

public class Department
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}