using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;

public class StockUpScenario
{
    private readonly PlaywrightSourceOptions _options;
    private readonly PlaywrightBrowserFactory _browserFactory;
    private readonly ILogger<StockUpScenario> _logger;

    public StockUpScenario(
        PlaywrightSourceOptions options,
        PlaywrightBrowserFactory browserFactory,
        ILogger<StockUpScenario> logger
    )
    {
        _options = options;
        _browserFactory = browserFactory;
        _logger = logger;
    }

    public async Task<StockUpRecord> RunAsync(StockUpRequest request)
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        await using var browser = await _browserFactory.CreateAsync(playwright);

        _logger.LogDebug("Browser launched successfully, creating new page...");
        var page = await browser.NewPageAsync();
        _logger.LogDebug("Page created successfully");

        await page.GotoAsync(_options.ShopEntryUrl);
        await page.WaitForLoadStateAsync();

        await page.ClickAsync("[placeholder='E-mail']");
        await page.FillAsync("[placeholder='E-mail']", _options.Login);
        await page.PressAsync("[placeholder='E-mail']", "Tab");
        await page.FillAsync("[placeholder='Vaše heslo']", _options.Password);
        await page.ClickAsync("role=button >> text=Přihlášení");
        await page.WaitForLoadStateAsync();

        _logger.LogDebug("Login successful");

        await page.GotoAsync($"{_options.ShopEntryUrl}/sklad");
        await page.WaitForLoadStateAsync();
        await page.ClickAsync("text=Naskladnění");
        await page.WaitForLoadStateAsync();
        await page.FillAsync("input[name='documentNumber']", request.StockUpId);

        foreach (var product in request.Products)
        {
            await page.FillAsync("input[name='stockingSearch']", product.ProductCode);
            await page.PressAsync("input[name='stockingSearch']", "Enter");
            await page.WaitForSelectorAsync(".cashdesk-search-result",
                new PageWaitForSelectorOptions { Timeout = 10000 });
            await page.ClickAsync(".cashdesk-products-listing > .product");
            await page.FillAsync("text=Množství",
                product.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        await page.ClickAsync("[data-testid='buttonAddItemsToStock']");

        return new StockUpRecord();
    }
}