namespace Anela.Heblo.Domain.Entities;

public class Supplier
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string? Note { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Url { get; set; }
    public string? Description { get; set; }
}