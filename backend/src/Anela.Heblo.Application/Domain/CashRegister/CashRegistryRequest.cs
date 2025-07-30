namespace Anela.Heblo.Application.Domain.CashRegister;

public class CashRegistryRequest
{
    public int Month { get; set; }
    public int Year { get; set; }
    public int[] RegistersId { get; set; }
}