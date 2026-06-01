namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class LotDto
{
    public string? LotCode { get; set; }
    public decimal Amount { get; set; }
    public DateOnly? Expiration { get; set; }
}