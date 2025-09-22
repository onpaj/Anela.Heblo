using System;

namespace Anela.Heblo.IssuedInvoices;

public class CashRegisterOrderResult
{
    public int CashRegisterId { get; set; }
    public string User { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public BillingMethod TransactionType { get; set; }
    public string OrderNo { get; set; }
}