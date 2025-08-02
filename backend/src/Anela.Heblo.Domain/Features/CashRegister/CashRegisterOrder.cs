using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Domain.Features.CashRegister;

public class CashRegisterOrder
{
    public int CashRegisterId { get; set; }
    public string User { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public BillingMethod TransactionType { get; set; }
    public string OrderNo { get; set; }
}