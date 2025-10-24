namespace Anela.Heblo.Application.Features.OrgChart.Contracts;

/// <summary>
/// Represents a position in the organizational structure
/// </summary>
public class PositionDto
{
    /// <summary>
    /// Unique identifier for the position
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Position title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Position description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Hierarchical level (1 = top level, higher numbers = lower in hierarchy)
    /// </summary>
    public int? Level { get; set; }

    /// <summary>
    /// ID of the parent position in the hierarchy
    /// </summary>
    public string? ParentPositionId { get; set; }

    /// <summary>
    /// Department name
    /// </summary>
    public string Department { get; set; } = string.Empty;

    /// <summary>
    /// List of employees in this position
    /// </summary>
    public List<EmployeeDto> Employees { get; set; } = new();

    /// <summary>
    /// Optional URL to position description (e.g., SharePoint document)
    /// </summary>
    public string? Url { get; set; }
}
