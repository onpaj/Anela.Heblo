namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public class SupplierDto
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string? Note { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Url { get; set; }
}