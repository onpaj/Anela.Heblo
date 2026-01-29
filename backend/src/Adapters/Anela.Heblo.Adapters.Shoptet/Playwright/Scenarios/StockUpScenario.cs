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

        // Try to clear all existing items first (if button exists and is visible)
        _logger.LogDebug("Checking if clear items button exists and is visible");
        try
        {
            var clearButton = page.GetByText("Smazat všechny položky");
            await clearButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 2000 });

            _logger.LogDebug("Clear button found, clicking it");
            await clearButton.ClickAsync();

            // Handle confirmation dialog if it appears
            try
            {
                await page.WaitForSelectorAsync("[role='dialog'], .modal, .confirmation-dialog",
                    new PageWaitForSelectorOptions { Timeout = 3000 });

                // Look for confirmation button (OK/Ano/Potvrdit)
                var confirmButton = page.Locator("button").Filter(new() { HasText = "OK" })
                    .Or(page.Locator("button").Filter(new() { HasText = "Ano" }))
                    .Or(page.Locator("button").Filter(new() { HasText = "Potvrdit" }))
                    .Or(page.Locator("button").Filter(new() { HasText = "Smazat" }));

                await confirmButton.First.ClickAsync();
                await page.WaitForTimeoutAsync(1000);
                _logger.LogDebug("Confirmation dialog handled successfully");
            }
            catch (TimeoutException)
            {
                _logger.LogDebug("No confirmation dialog appeared, continuing");
            }
        }
        catch (TimeoutException)
        {
            _logger.LogDebug("Clear button not found or not visible, continuing with product entry");
        }

        foreach (var product in request.Products)
        {
            await page.FillAsync("input[name='stockingSearch']", product.ProductCode);
            await page.PressAsync("input[name='stockingSearch']", "Enter");
            await page.WaitForSelectorAsync(".cashdesk-search-result",
                new PageWaitForSelectorOptions { Timeout = 60000 });

            // Wait for products to be actually loaded and clickable
            await page.WaitForSelectorAsync(".cashdesk-products-listing > .product",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });
            await page.WaitForTimeoutAsync(500); // Additional wait for products to stabilize

            // Use locator for more robust element handling
            var productLocator = page.Locator(".cashdesk-products-listing > .product").First;
            await productLocator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
            await productLocator.ClickAsync();

            // Wait for the quantity input field to be available and attached
            await page.WaitForSelectorAsync(".stock-amount", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });

            // Wait for any DOM mutations to settle
            await page.WaitForTimeoutAsync(500);

            // Find the specific quantity input for positive amounts with retry logic
            const int maxRetries = 3;
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    // Re-query the element each time to ensure it's fresh
                    var quantityInput = page.GetByRole(AriaRole.Textbox, new() { Name = "Množství" });

                    // Wait for the element to be attached and stable
                    await quantityInput.WaitForAsync(new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Attached,
                        Timeout = 60000
                    });

                    // Scroll to the quantity input to make sure it's visible
                    await quantityInput.ScrollIntoViewIfNeededAsync();

                    // Wait a bit for the scroll to complete
                    await page.WaitForTimeoutAsync(300);

                    // Clear any existing value and fill the amount
                    await quantityInput.ClearAsync();
                    await quantityInput.FillAsync(product.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));

                    // Success - break out of retry loop
                    break;
                }
                catch (PlaywrightException ex) when (ex.Message.Contains("not attached") && retry < maxRetries - 1)
                {
                    _logger.LogWarning($"Element detached on attempt {retry + 1}, retrying...");
                    await page.WaitForTimeoutAsync(1000); // Wait before retry
                }
            }

            // Wait a moment for the value to be processed
            await page.WaitForTimeoutAsync(500);
        }

        if (!_options.DryRun)
        {
            await page.ClickAsync("[data-testid='buttonAddItemsToStock']");

            // CRITICAL FIX: Wait for operation to complete before closing browser
            // Without this wait, browser closes immediately and Shoptet may not save documentNumber
            // This prevents race condition where stock-up completes but without document number
            _logger.LogDebug("Waiting for stock-up operation to complete for document {DocumentNumber}", request.StockUpId);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 30000 });

            _logger.LogInformation("Stock-up operation completed for document {DocumentNumber}", request.StockUpId);
        }
        else
        {
            _logger.LogInformation("DRY RUN: StockUp");
        }

        return new StockUpRecord();
    }
}