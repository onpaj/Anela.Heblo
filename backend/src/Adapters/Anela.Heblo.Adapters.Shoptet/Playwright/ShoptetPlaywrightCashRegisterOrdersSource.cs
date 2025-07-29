using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Anela.Heblo.Invoices;
using Anela.Heblo.IssuedInvoices;
using Anela.Heblo.IssuedInvoices.Model;

namespace Anela.Heblo.Adapters.Shoptet.Playwright;

public class ShoptetPlaywrightCashRegisterOrdersSource : ICashRegisterOrdersSource
{
    private readonly CashRegisterStatisticsScenario _scenario;

    public ShoptetPlaywrightCashRegisterOrdersSource(CashRegisterStatisticsScenario scenario)
    {
        _scenario = scenario;
    }

    public async Task<List<CashRegisterOrder>> GetAllAsync(CashRegistryRequest query)
    {
        var registers = query.RegistersId.Select(s => new CashRegister() { Id = s}).ToList();
        var result = await _scenario.RunAsync(registers, query.Year, query.Month);

        return result.Orders;
    }
}