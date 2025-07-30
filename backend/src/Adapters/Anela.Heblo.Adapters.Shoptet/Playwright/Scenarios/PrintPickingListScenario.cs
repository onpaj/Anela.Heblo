using Anela.Heblo.Adapters.Shoptet.Playwright.Model;
using Anela.Heblo.Application.Domain.Logistics.Picking;
using Anela.Heblo.Application.Domain.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;

public class PrintPickingListScenario
{
    private readonly PlaywrightSourceOptions _options;
    private readonly ILogger<PrintPickingListScenario> _logger;
    private readonly TimeProvider _timeProvider;

    private const string ExportFileNameHeaderName = "x-export-file-name";

    private TaskCompletionSource<bool> semaphore;

    public PrintPickingListScenario(
        PlaywrightSourceOptions options,
        ILogger<PrintPickingListScenario> logger,
        TimeProvider timeProvider
    )
    {
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<PrintPickingListResult> RunAsync(List<Shipping> shippings, int maxPageSize, int? sourceStateId = null, int? desiredStateId = null)
    {
        // Make sure dir exists
        Directory.CreateDirectory(_options.PdfTmpFolder);
        
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
     
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions()
        {
            Headless = _options.Headless,
        });
        var page = await browser.NewPageAsync();
        await InitPage(page, browser);

        await page.GotoAsync(_options.ShopEntryUrl);
        await page.WaitForLoadStateAsync();

        await page.ClickAsync("[placeholder='E-mail']");
        await page.FillAsync("[placeholder='E-mail']", _options.Login);
        await page.PressAsync("[placeholder='E-mail']", "Tab");
        await page.FillAsync("[placeholder='Vaše heslo']", _options.Password);
        await page.ClickAsync("role=button >> text=Přihlášení");

        _logger.LogDebug("Login successful");

        var exportList = new List<string>();
        var totalCount = 0;
        
        
        foreach (var shipping in shippings)
        {
            int found;
            var pageCounter = 0;
            do
            {
                // Select top x for print
                await page.GotoAsync($"{_options.ShopEntryUrl}prehled-objednavek/{sourceStateId}/?f[shippingId]={shipping.Id}");
                found = await SelectTopX(page, shipping.Id, shipping.PageSize);

                if (found > 0)
                {
                    // Print them to PDF
                    var filename = $"{_timeProvider.GetFilenameTimestamp()}_{shipping.Carrier.ToString()}_{shipping.Id.ToString()}_{pageCounter++.ToString().PadLeft(2, '0')}.pdf";
                    var result = await PrintSelected(page, filename);
                    _logger.LogDebug("Finished print to file {Filename} for shipping={ShippingId}", filename, shipping.Id);

                    if (result && desiredStateId != null)
                    {
                        // Select them again (so far no other way around)
                        found = await SelectTopX(page, shipping.Id, shipping.PageSize);
                        
                        // Change states
                        result = await ChangeStateSelected(page, desiredStateId.Value);
                        _logger.LogDebug("Changing state to {DesiredState} for shipping={ShippingId}", desiredStateId, shipping.Id);
                    }
                    if (!result)
                        throw new Exception();
                    
                    exportList.Add(GetAbsolutePath(filename));
                    totalCount += found;
                }
                
            } while (found >= maxPageSize);
        }

        return new PrintPickingListResult()
        {
            ExportedFiles = exportList,
            TotalCount = totalCount,
        };
    }

    private async Task InitPage(IPage page, IBrowser browser)
    {
        page.SetDefaultTimeout(300000); // Set timeout
        page.Console += (_, msg) => { Console.WriteLine(msg.Text); };
        // Catch print request running in another thread
        page.Response += async (_, response) =>
        {
            if (response.Request.Url.Contains("?type=expedition&ids="))
            {
                await HandlePrintRequest(response, browser);
            }
        };
        
        // Disable print dialog
        await page.RouteAsync("**/MassPrint/*", RewritePrintDialog);

        // Auto accept confirm dialog
        page.Dialog += async (_, dialog) =>
        {
            Console.WriteLine($"Dialog message: {dialog.Message}");
            await dialog.AcceptAsync();
        };
    }

    private async Task<int> SelectTopX(IPage page, int shippingId, int pageSize)
    {
        var found = 0;
        // await page.WaitForSelectorAsync("div.pagination__founds strong");
        await page.WaitForSelectorAsync(".pageGrid__footer.footer");


        _logger.LogDebug("Listing orders for shipping {ShippingId}", shippingId);

        for (int i = 0; i < pageSize; i++)
        {
            if (await CheckIfPresent(page, $"input[name=\"orderId[{i}]\"]"))
                found++;
        }

        _logger.LogDebug("Found {OrderCount} for shipping={ShippingId}", found, shippingId);

        return found;
    }


    private async Task<bool> ChangeStateSelected(IPage page, int desiredStateId)
    {
        await page.ClickAsync("text=Funkce");
        await page.ClickAsync(".massAction__submenuHeader:has-text('Stav')");
        await page.ClickAsync($"a[rel='massStatusChange|{desiredStateId}']");
        
        await page.WaitForSelectorAsync(".systemMessage.systemMessage--success .systemMessage__text");

        return true;
    }

    private async Task<bool> PrintSelected(IPage page, string filename)
    {
        await page.ClickAsync("text=Funkce");
        await page.ClickAsync("text=Tisk");

        await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
        {
            { ExportFileNameHeaderName, filename },
        });

        semaphore = new TaskCompletionSource<bool>();
        
        await page.ClickAsync("text=Expedice");
        await page.WaitForResponseAsync(r => true, new PageWaitForResponseOptions() { Timeout = 30000 });

        await semaphore.Task;

        await page.ReloadAsync();
        return true;
    }


    private async Task<bool> CheckIfPresent(IPage page, string locator)
    {
        var elementHandle = await page.QuerySelectorAsync(locator);
        if (elementHandle != null)
        {
            // Element found, proceed with your actions, for example:
            await elementHandle.CheckAsync();
            return true;
        }

        return false;
    }
    
    
    private async Task HandlePrintRequest(IResponse response, IBrowser browser)
    {
        _logger.LogDebug("Printing export page {Url}", response.Request.Url);
        var body = await response.TextAsync();

        var newPage = await browser.NewPageAsync();

        await newPage.SetContentAsync(body);

        var filenameHeader = response.Request.Headers.SingleOrDefault(s => s.Key == ExportFileNameHeaderName);
        
        await newPage.PdfAsync(new PagePdfOptions() { Path = GetAbsolutePath(filenameHeader.Value) });

        await newPage.CloseAsync();
        _logger.LogDebug("Page {Url} extracted to {FileName}", response.Request.Url, filenameHeader.Value);
        semaphore.SetResult(true);
    }
    
    private async Task RewritePrintDialog(IRoute route)
    {
        var response = await route.FetchAsync();
        var body = await response.TextAsync();
        var headers = response.Headers;

        if (route.Request.Url.Contains("?type=expedition&ids="))
        {
            body = body.Replace("window.print(); parent.$('body').trigger('printDialogOpened');", "");
            _logger.LogDebug("Rewriting window.print() for {Url}", route.Request.Url);
        }

        await route.FulfillAsync(new()
        {
            // Pass all fields from the response.
            Response = response,
            // Override response body.
            Body = body,
            // Force content type to be html.
            Headers = headers,
        });
    }

    private string GetAbsolutePath(string filename)
    {
        return Path.Combine(_options.PdfTmpFolder, filename);
    }
}