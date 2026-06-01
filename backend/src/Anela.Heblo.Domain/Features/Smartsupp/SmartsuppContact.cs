namespace Anela.Heblo.Domain.Features.Smartsupp;

public class SmartsuppContact
{
    public string Id { get; set; } = null!;
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public DateTime? BannedAt { get; set; }
    public string? BannedBy { get; set; }
    public bool GdprApproved { get; set; }
    public string? TagsJson { get; set; }
    public string? PropertiesJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime SyncedAt { get; set; }
}
