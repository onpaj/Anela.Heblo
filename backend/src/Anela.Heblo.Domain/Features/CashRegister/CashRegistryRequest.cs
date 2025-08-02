namespace Anela.Heblo.Domain.Features.CashRegister;

public class CashRegistryRequest
{
    public int Month { get; set; }
    public int Year { get; set; }
    public int[] RegistersId { get; set; }
}