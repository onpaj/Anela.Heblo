namespace Anela.Heblo.Application.Features.OrgChart.Contracts;

/// <summary>
/// Represents the complete organizational structure
/// </summary>
public class OrganizationDto
{
    /// <summary>
    /// Organization name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// List of all positions in the organization
    /// </summary>
    public List<PositionDto> Positions { get; set; } = new();
}
