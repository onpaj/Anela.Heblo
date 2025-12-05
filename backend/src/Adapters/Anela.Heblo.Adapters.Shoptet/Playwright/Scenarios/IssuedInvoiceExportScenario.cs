using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;

public class IssuedInvoiceExportScenario
{
    private readonly PlaywrightSourceOptions _options;
    private readonly PlaywrightBrowserFactory _browserFactory;
    private readonly ILogger<ShoptetPlaywrightInvoiceSource> _logger;

    public IssuedInvoiceExportScenario(
        PlaywrightSourceOptions options,
        PlaywrightBrowserFactory browserFactory,
        ILogger<ShoptetPlaywrightInvoiceSource> logger
    )
    {
        _options = options;
        _browserFactory = browserFactory;
        _logger = logger;
    }

    public async Task<string> RunAsync(IssuedInvoiceSourceQuery query)
    {
        var currency = query.Currency == CurrencyCode.EUR.ToString() ? "3" : "2";
        var outputFile = Path.GetTempFileName();
        IPage? page = null;
        IBrowser? browser = null;
        IPlaywright? playwright = null;

        try
        {
            _logger.LogInformation("Starting extracting query {RequestId}", query.RequestId);
            playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            browser = await _browserFactory.CreateAsync(playwright);
            page = await browser.NewPageAsync();

            var timeout = TimeSpan.FromMinutes(5);
            var navigationTimeout = TimeSpan.FromSeconds(30);
            // Set more reasonable timeouts
            page.SetDefaultTimeout((float)timeout.TotalMilliseconds); // 5 minutes seconds default timeout
            page.SetDefaultNavigationTimeout((float)navigationTimeout.TotalMilliseconds); // 30 seconds for navigation

            await page.GotoAsync(_options.ShopEntryUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });

            // Wait for login form and fill credentials
            await page.WaitForSelectorAsync("[placeholder='E-mail']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible });
            await page.ClickAsync("[placeholder='E-mail']");
            await page.FillAsync("[placeholder='E-mail']", _options.Login);
            await page.PressAsync("[placeholder='E-mail']", "Tab");
            await page.FillAsync("[placeholder='Vaše heslo']", _options.Password);
            await page.ClickAsync("role=button >> text=Přihlášení");

            // Wait for navigation after login
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 30000 });
            await page.GotoAsync($"{_options.ShopEntryUrl}/danove-doklady/", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60000
            });

            var openMenuElement = await page.WaitForSelectorAsync(".open-menu", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible });
            await openMenuElement.HoverAsync();
            await Task.Delay(1000); // Small delay for menu animation

            await page.ClickAsync("text=Export dokladů");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await page.WaitForSelectorAsync("a[data-testid=buttonExport]", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
            if (query.QueryByDate)
            {
                await page.PressAsync("body", "Tab");
                await page.PressAsync("body", "Tab");
                await page.PressAsync("body", "Tab");
                await page.PressAsync("body", "Tab");

                var activeElement1 = await page.EvaluateHandleAsync("document.activeElement");
                await activeElement1.AsElement().FillAsync(query.DateFromString);
                await activeElement1.DisposeAsync();

                await page.PressAsync("body", "Tab");

                var activeElement2 = await page.EvaluateHandleAsync("document.activeElement");
                await activeElement2.AsElement().FillAsync(query.DateToString);
                await activeElement2.DisposeAsync();
            }

            if (query.QueryByInvoice)
            {
                var activeElement3 = await page.EvaluateHandleAsync("document.activeElement");
                await activeElement3.AsElement().FillAsync(query.InvoiceId);
                await activeElement3.DisposeAsync();

                await page.PressAsync("body", "Tab");

                var activeElement4 = await page.EvaluateHandleAsync("document.activeElement");
                await activeElement4.AsElement().FillAsync(query.InvoiceId);
                await activeElement4.DisposeAsync();
            }

            if (currency != "2")
            {
                await page.Locator("#main-modal-form").GetByLabel("MěnaCZKEUR").SelectOptionAsync(currency);
            }

            var downloadEventTask = page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = (float)timeout.TotalMilliseconds });
            await page.ClickAsync("role=link >> text=Exportovat");
            var downloadEvent = await downloadEventTask;

            await downloadEvent.SaveAsAsync(outputFile);

            var content = await File.ReadAllTextAsync(outputFile);

            _logger.LogInformation("Data for batch {RequestId} extracted - Total of {ContentLength}", query.RequestId,
                content.Length);

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during invoice export scenario for request {RequestId}", query.RequestId);
            throw;
        }
        finally
        {
            // Cleanup
            try
            {
                if (page != null)
                {
                    await page.CloseAsync();
                }
                if (browser != null)
                {
                    await browser.DisposeAsync();
                }
                playwright?.Dispose();
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Error during cleanup for request {RequestId}", query.RequestId);
            }

            // Delete temp file
            try
            {
                if (File.Exists(outputFile))
                {
                    File.Delete(outputFile);
                }
            }
            catch (Exception fileEx)
            {
                _logger.LogWarning(fileEx, "Failed to delete temp file {OutputFile}", outputFile);
            }
        }
    }
}