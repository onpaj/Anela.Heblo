using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;

public class VerifyStockUpScenario
{
    private readonly PlaywrightSourceOptions _options;
    private readonly PlaywrightBrowserFactory _browserFactory;
    private readonly ILogger<VerifyStockUpScenario> _logger;

    public VerifyStockUpScenario(
        PlaywrightSourceOptions options,
        PlaywrightBrowserFactory browserFactory,
        ILogger<VerifyStockUpScenario> logger
    )
    {
        _options = options;
        _browserFactory = browserFactory;
        _logger = logger;
    }

    public async Task<bool> RunAsync(string documentNumber)
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        await using var browser = await _browserFactory.CreateAsync(playwright);

        _logger.LogDebug("Browser launched successfully for verification, creating new page...");
        var page = await browser.NewPageAsync();
        _logger.LogDebug("Page created successfully");

        // Login
        await page.GotoAsync(_options.ShopEntryUrl);
        await page.WaitForLoadStateAsync();

        await page.ClickAsync("[placeholder='E-mail']");
        await page.FillAsync("[placeholder='E-mail']", _options.Login);
        await page.PressAsync("[placeholder='E-mail']", "Tab");
        await page.FillAsync("[placeholder='Vaše heslo']", _options.Password);
        await page.ClickAsync("role=button >> text=Přihlášení");
        await page.WaitForLoadStateAsync();

        _logger.LogDebug("Login successful, navigating to stock history...");

        // Navigate to stock history (Historie tab in /admin/sklad)
        await page.GotoAsync($"{_options.ShopEntryUrl}/admin/sklad");
        await page.WaitForLoadStateAsync();

        // Click on "Historie" tab
        try
        {
            var historieTab = page.Locator("text=Historie").Or(page.Locator("a:has-text('Historie')"));
            await historieTab.First.ClickAsync(new LocatorClickOptions { Timeout = 10000 });
            await page.WaitForLoadStateAsync();
            _logger.LogDebug("Navigated to Historie tab");
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning($"Could not find 'Historie' tab, trying alternative navigation: {ex.Message}");
            // Alternative: try direct URL if tab navigation fails
            await page.GotoAsync($"{_options.ShopEntryUrl}/admin/sklad/historie");
            await page.WaitForLoadStateAsync();
        }

        // Fill document number filter
        _logger.LogDebug($"Filtering by document number: {documentNumber}");
        try
        {
            // Find the document number filter input (might be named "documentNumber", "cislo", or similar)
            var documentFilter = page.Locator("input[name='documentNumber']")
                .Or(page.Locator("input[placeholder*='Číslo dokladu']"))
                .Or(page.Locator("input[placeholder*='doklad']"));

            await documentFilter.First.FillAsync(documentNumber);
            _logger.LogDebug("Document number filled in filter");

            // Click filter button
            var filterButton = page.Locator("button:has-text('Filtrovat')")
                .Or(page.Locator("button:has-text('Hledat')")
                .Or(page.Locator("button[type='submit']")));

            await filterButton.First.ClickAsync();
            await page.WaitForLoadStateAsync();
            await page.WaitForTimeoutAsync(1000); // Wait for results to load

            _logger.LogDebug("Filter applied, checking results...");
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning($"Could not find filter controls: {ex.Message}");
            return false;
        }

        // Check if table contains at least one row with the document number
        try
        {
            // Look for table rows containing the document number
            var resultRows = page.Locator($"tr:has-text('{documentNumber}')")
                .Or(page.Locator($"td:has-text('{documentNumber}')"));

            var count = await resultRows.CountAsync();
            var exists = count > 0;

            _logger.LogInformation($"Verification result for document {documentNumber}: {(exists ? "FOUND" : "NOT FOUND")} ({count} rows)");

            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking for document {documentNumber} in stock history");
            return false;
        }
    }
}
