using Anela.Heblo.Application.Domain.IssuedInvoices;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;

public class IssuedInvoiceExportScenario
{
    private readonly PlaywrightSourceOptions _options;
    private readonly ILogger<ShoptetPlaywrightInvoiceSource> _logger;

    public IssuedInvoiceExportScenario(
        PlaywrightSourceOptions options,
        ILogger<ShoptetPlaywrightInvoiceSource> logger
    )
    {
        _options = options;
        _logger = logger;
    }
    
    public async Task<string> RunAsync(IssuedInvoiceSourceQuery query)
    {
        var currency = query.Currency == CurrencyCode.EUR.ToString() ? "3" : "2";
        var outputFile = Path.GetTempFileName();

        _logger.LogInformation("Starting extracting query {RequestId}", query.RequestId);
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions()
        {
            Headless = _options.Headless,
        });
        var page = await browser.NewPageAsync();

        page.SetDefaultTimeout(300000); // Set timeout

        await page.GotoAsync(_options.ShopEntryUrl);
        await page.WaitForLoadStateAsync();

        await page.ClickAsync("[placeholder='E-mail']");
        await page.FillAsync("[placeholder='E-mail']", _options.Login);
        await page.PressAsync("[placeholder='E-mail']", "Tab");
        await page.FillAsync("[placeholder='Vaše heslo']", _options.Password);
        await page.ClickAsync("role=button >> text=Přihlášení");
        await page.ClickAsync("text=Objednávky");
        await page.ClickAsync("text=Daňové doklady");

        var openMenuElement = await page.WaitForSelectorAsync(".open-menu");
        await openMenuElement.HoverAsync();


        await page.ClickAsync("text=Export dokladů");
        
        await page.WaitForSelectorAsync("a[data-testid=buttonExport]");
        // try
        // {
        //     await page.WaitForSelectorAsync("a.btn.btn-md.btn-action.submit-js[title='Exportovat'][rel='export']",
        //         new PageWaitForSelectorOptions() { Timeout = 2000 });
        // }
        // catch (Exception ex)
        // {
        // }
        if (query.QueryByDate)
        {
            await page.PressAsync("body", "Tab");
            await page.PressAsync("body", "Tab");
            await page.PressAsync("body", "Tab");
            await page.PressAsync("body", "Tab");

            await page.EvaluateHandleAsync("document.activeElement").Result.AsElement().FillAsync(query.DateFromString);
            await page.PressAsync("body", "Tab");
            await page.EvaluateHandleAsync("document.activeElement").Result.AsElement().FillAsync(query.DateToString);
        }

        if (query.QueryByInvoice)
        {
            await page.EvaluateHandleAsync("document.activeElement").Result.AsElement().FillAsync(query.InvoiceId);
            await page.PressAsync("body", "Tab");
            await page.EvaluateHandleAsync("document.activeElement").Result.AsElement().FillAsync(query.InvoiceId);
        }

        if (currency != "2")
        {
            await page.Locator("#main-modal-form").GetByLabel("MěnaCZKEUR").SelectOptionAsync(currency);
        }

        var downloadEventTask = page.WaitForDownloadAsync();
        await page.ClickAsync("role=link >> text=Exportovat");
        var downloadEvent = await downloadEventTask;

        await downloadEvent.SaveAsAsync(outputFile);

        var content = await File.ReadAllTextAsync(outputFile);

        _logger.LogInformation("Data for batch {RequestId} extracted - Total of {ContentLength}", query.RequestId,
            content.Length);

        File.Delete(outputFile);

        
        return content;
    }
}