using System.Collections.Generic;
using System.Threading.Tasks;
using Anela.Heblo.IssuedInvoices;
using Anela.Heblo.IssuedInvoices.Model;

namespace Anela.Heblo.Invoices;

public interface ICashRegisterOrdersSource
{
    Task<List<CashRegisterOrder>> GetAllAsync(CashRegistryRequest query);
}