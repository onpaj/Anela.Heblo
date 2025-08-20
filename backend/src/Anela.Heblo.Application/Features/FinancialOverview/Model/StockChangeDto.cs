namespace Anela.Heblo.Application.Features.FinancialOverview.Model;

public class StockChangeDto
{
    /// <summary>
    /// Stock value change for Materials warehouse (MATERIAL - ID 5)
    /// </summary>
    public decimal Materials { get; set; }

    /// <summary>
    /// Stock value change for Semi-products warehouse (POLOTOVARY - ID 20)
    /// </summary>
    public decimal SemiProducts { get; set; }

    /// <summary>
    /// Stock value change for Products/Goods warehouse (ZBOZI - ID 4)
    /// </summary>
    public decimal Products { get; set; }
}