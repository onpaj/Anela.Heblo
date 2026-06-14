namespace Anela.Heblo.Application.Features.Authorization.UseCases;

public class GroupSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int PermissionCount { get; set; }
    public int ParentCount { get; set; }
    public int MemberCount { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<Guid> ParentGroupIds { get; set; } = new();
}

public class GroupDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<Guid> ParentGroupIds { get; set; } = new();
}
