using Anela.Heblo.Application.Domain.Catalog.Stock;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;

public class StockUpScenario
{
    private readonly PlaywrightSourceOptions _options;
    private readonly ILogger<StockUpScenario> _logger;

    public StockUpScenario(
        PlaywrightSourceOptions options,
        ILogger<StockUpScenario> logger
    )
    {
        _options = options;
        _logger = logger;
    }

    public async Task<StockUpRecord> RunAsync(StockUpRequest request)
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions()
        {
            Headless = _options.Headless,
        });
        var page = await browser.NewPageAsync();

        await page.GotoAsync(_options.ShopEntryUrl);
        await page.WaitForLoadStateAsync();

        await page.ClickAsync("[placeholder='E-mail']");
        await page.FillAsync("[placeholder='E-mail']", _options.Login);
        await page.PressAsync("[placeholder='E-mail']", "Tab");
        await page.FillAsync("[placeholder='Vaše heslo']", _options.Password);
        await page.ClickAsync("role=button >> text=Přihlášení");

        _logger.LogDebug("Login successful");

        await page.GotoAsync($"{_options.ShopEntryUrl}/sklad");
        await page.WaitForLoadStateAsync();
        await page.ClickAsync("text=Naskladnění");
        await page.FillAsync("input[name='documentNumber']", request.StockUpId);
        await page.FillAsync("input[name='stockingSearch']", request.Product);
        await page.PressAsync("input[name='stockingSearch']", "Enter");
        await page.WaitForSelectorAsync(".cashdesk-search-result");
        await page.ClickAsync(".cashdesk-products-listing > .product");
        await page.FillAsync("text=Množství", request.Amount.ToString());
        await page.ClickAsync("[data-testid='buttonAddItemsToStock']");

        return new StockUpRecord()
        {
        };
    }
}