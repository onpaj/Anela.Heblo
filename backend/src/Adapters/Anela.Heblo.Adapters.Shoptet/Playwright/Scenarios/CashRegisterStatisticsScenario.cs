using System.Globalization;
using Anela.Heblo.Adapters.Shoptet.Playwright.Model;
using Anela.Heblo.IssuedInvoices;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;

public class CashRegisterStatisticsScenario
{
    private readonly PlaywrightSourceOptions _options;
    private readonly ILogger<CashRegisterStatisticsScenario> _logger;

    private const string ExportFileNameHeaderName = "x-export-file-name";

    private TaskCompletionSource<bool> semaphore;

    public CashRegisterStatisticsScenario(
        PlaywrightSourceOptions options,
        ILogger<CashRegisterStatisticsScenario> logger
    )
    {
        _options = options;
        _logger = logger;
    }

    public async Task<CashRegisterStatisticsResult> RunAsync(List<CashRegister> registers, int year, int month)
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

        var result = new CashRegisterStatisticsResult();
        foreach (var register in registers)
        {
            int found;
            var pageCounter = 0;

            await page.GotoAsync($"{_options.ShopEntryUrl}statistika-pokladny/?f[monthYear]={month}%2F{year}&f[cashDeskId]={register.Id}");
            
            var html = await page.ContentAsync();

            var orders = ParseOrders(html);
            result.Orders.AddRange(orders.Select(s => new CashRegisterOrderResult()
            {
                CashRegisterId = register.Id,
                User = s.User,
                Date = s.Date,
                Amount = s.Amount,
                TransactionType = ParseTransactionType(s.TransactionType),
                OrderNo = s.OrderNo
            }));
        }

        return result;
    }

    private BillingMethod ParseTransactionType(string argTransactionType)
    {
        return BillingMethod.Cash;
    }

    private List<CashRegisterStatisticsLine> ParseOrders(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var table = doc.DocumentNode.SelectSingleNode("//*[@id='detail']/div/table");

        var list = new List<CashRegisterStatisticsLine>();

        foreach (var rows in table.SelectNodes("tbody/tr").Skip(1)) 
        {
            var columns = rows.SelectNodes("td").Select(td => td.InnerText.Trim()).ToArray();

            var data = new CashRegisterStatisticsLine
            {
                User = GetUserName(columns),
                Date = GetDate(columns),
                Amount = GetAmount(columns),
                OrderNo = GetOrderNo(columns),
                TransactionType = GetTransactionType(columns),
            };

            list.Add(data);
        }

        return list;
    }


    private string GetUserName(string[] columns) => columns[0];
    private DateTime GetDate(string[] columns) => DateTime.ParseExact(columns[1], "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
    private decimal GetAmount(string[] columns) => Decimal.Parse(columns[4].Replace(" Kč", "").Replace(" ", ""));
    private string GetOrderNo(string[] columns) => columns[8].Split("\n")[0];
    private string GetTransactionType(string[] columns) => columns[7];

    private async Task InitPage(IPage page, IBrowser browser)
    {
        page.SetDefaultTimeout(300000); // Set timeout
        page.Console += (_, msg) => { Console.WriteLine(msg.Text); };
        // Catch print request running in another thread
    }
}