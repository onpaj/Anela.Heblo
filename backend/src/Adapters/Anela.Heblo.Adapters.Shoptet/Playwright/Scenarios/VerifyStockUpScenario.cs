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

        // Navigate to stock history (Historie tab in /sklad)
        await page.GotoAsync($"{_options.ShopEntryUrl}/sklad");
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
            await page.GotoAsync($"{_options.ShopEntryUrl}/sklad/historie");
            await page.WaitForLoadStateAsync();
        }

        // Expand filter panel first
        _logger.LogDebug($"Filtering by document number: {documentNumber}");
        try
        {
            // Step 1: Click toggle button to expand filters
            var toggleButton = page.Locator("[data-testid='buttonToggleFilter']");
            await toggleButton.ClickAsync(new LocatorClickOptions { Timeout = 10000 });
            await page.WaitForTimeoutAsync(500); // Wait for filter panel to expand
            _logger.LogDebug("Filter panel expanded");

            // Step 2: Fill document number in the now-visible input
            var documentFilter = page.Locator("input[name='documentNumber']");
            await documentFilter.FillAsync(documentNumber);
            _logger.LogDebug("Document number filled in filter");

            // Step 3: Click the filter apply button
            var filterButton = page.Locator("[data-testid='buttonActivateFilter']");
            await filterButton.ClickAsync();
            await page.WaitForLoadStateAsync();
            await page.WaitForTimeoutAsync(1000); // Wait for results to load

            _logger.LogDebug("Filter applied, checking results...");
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning($"Could not interact with filter controls: {ex.Message}");
            return false;
        }

        // Check if table contains any rows (document number is not displayed in table, only used as filter)
        try
        {
            // Check if table has any data rows (not just headers)
            var tableRows = page.Locator("table tbody tr");
            var rowCount = await tableRows.CountAsync();

            // Check for "no results" message
            var noResultsMessage = page.Locator("text=/žádné položky|nenalezen|no records/i");
            var hasNoResultsMessage = await noResultsMessage.CountAsync() > 0;

            // Document exists if there are rows in the table and no "no results" message
            var exists = rowCount > 0 && !hasNoResultsMessage;

            _logger.LogInformation($"Verification result for document {documentNumber}: {(exists ? "FOUND" : "NOT FOUND")} ({rowCount} rows in table, no results message: {hasNoResultsMessage})");

            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking for document {documentNumber} in stock history");
            return false;
        }
    }
}
