namespace Anela.Heblo.Adapters.OrgChart.Models;

internal sealed class OrgChartJsonModel
{
    public OrgChartJsonOrganization? Organization { get; set; }
}

internal sealed class OrgChartJsonOrganization
{
    public string? Name { get; set; }
    public List<OrgChartJsonPosition>? Positions { get; set; }
}

internal sealed class OrgChartJsonPosition
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int? Level { get; set; }
    public string? ParentPositionId { get; set; }
    public string? Department { get; set; }
    public List<OrgChartJsonEmployee>? Employees { get; set; }
    public string? Url { get; set; }
}

internal sealed class OrgChartJsonEmployee
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? StartDate { get; set; }
    public bool IsPrimary { get; set; }
    public string? Url { get; set; }
}
