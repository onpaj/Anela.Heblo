using System.Collections.Generic;

namespace Anela.Heblo.IssuedInvoices;

public class CashRegisterStatisticsResult
{
    public List<CashRegisterOrderResult> Orders { get; set; } = new ();
}