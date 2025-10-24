namespace Anela.Heblo.Application.Features.OrgChart.Contracts;

/// <summary>
/// Represents an employee in the organization
/// </summary>
public class EmployeeDto
{
    /// <summary>
    /// Unique identifier for the employee
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Employee full name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Employee email address
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Date when employee started in this position
    /// </summary>
    public string StartDate { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if this is the primary employee for the position
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Optional URL to employee profile (e.g., MS Entra profile)
    /// </summary>
    public string? Url { get; set; }
}
