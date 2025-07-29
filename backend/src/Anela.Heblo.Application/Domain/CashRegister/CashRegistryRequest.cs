namespace Anela.Heblo.IssuedInvoices.Model;

public class CashRegistryRequest
{
    public int Month { get; set; }
    public int Year { get; set; }
    public int[] RegistersId { get; set; }
}