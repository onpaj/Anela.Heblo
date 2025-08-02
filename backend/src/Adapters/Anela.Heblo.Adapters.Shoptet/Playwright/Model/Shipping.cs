using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Adapters.Shoptet.Playwright.Model;

public class Shipping
{
    public Carriers Carrier { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "??";

    public int PageSize { get; set; } = 8;
}