using System.Collections.Generic;

namespace Anela.Heblo.IssuedInvoices;

public class CashRegisterStatistics
{
    public List<CashRegisterOrder> Orders { get; set; } = new ();
}