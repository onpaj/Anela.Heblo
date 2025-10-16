using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;

public class StockTakingScenario
{
    private readonly PlaywrightSourceOptions _options;
    private readonly ILogger<StockTakingScenario> _logger;
    private readonly PlaywrightBrowserFactory _browserFactory;
    private readonly TimeProvider _timeProvider;

    public StockTakingScenario(
        PlaywrightSourceOptions options,
        ILogger<StockTakingScenario> logger,
        PlaywrightBrowserFactory browserFactory,
        TimeProvider timeProvider
    )
    {
        _options = options;
        _logger = logger;
        _browserFactory = browserFactory;
        _timeProvider = timeProvider;
    }

    public async Task<StockTakingRecord> RunAsync(EshopStockTakingRequest request)
    {
        // Definice selektorů
        var freeSelector = "body > div.pageGrid > div.pageGrid__content > div.section.section-1063 > form > fieldset > div.tableWrapper > table > tbody > tr > td:nth-child(5) > div:nth-child(1) > div > input";
        var reservedSelector = "body > div.pageGrid > div.pageGrid__content > div.section.section-1063 > form > fieldset > div.tableWrapper > table > tbody > tr > td:nth-child(5) > div:nth-child(2) > div > input";

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        await using var browser = await _browserFactory.CreateAsync(playwright);

        var page = await browser.NewPageAsync();

        await page.GotoAsync(_options.ShopEntryUrl);
        await page.WaitForLoadStateAsync();

        await page.ClickAsync("[placeholder='E-mail']");
        await page.FillAsync("[placeholder='E-mail']", _options.Login);
        await page.PressAsync("[placeholder='E-mail']", "Tab");
        await page.FillAsync("[placeholder='Vaše heslo']", _options.Password);
        await page.ClickAsync("role=button >> text=Přihlášení");

        _logger.LogDebug("Login successful");

        await page.GotoAsync($"{_options.ShopEntryUrl}/skladove-zasoby/?f[code]={request.ProductCode}");
        await page.WaitForLoadStateAsync();

        // Získání hodnoty z polí
        var actionsCellLocator = page.Locator("td.table__cell--actions").First;
        var originalLocator = actionsCellLocator.Locator("input[name^='originalAmounts']");
        var freeLocator = actionsCellLocator.Locator("input[name^='amounts']");
        var reservedLocator = actionsCellLocator.Locator("input").Last;

        var originalAmount = Convert.ToInt32(await originalLocator.InputValueAsync());
        var freeAmount = Convert.ToInt32(await freeLocator.InputValueAsync());
        var reservedAmount = Convert.ToInt32(await reservedLocator.GetAttributeAsync("placeholder"));

        var setAmount = request.TargetAmount - reservedAmount;
        // Výpočet a vyplnění nového množství
        await freeLocator.FillAsync(setAmount.ToString());

        if (!_options.DryRun)
        {
            // Uložení změn
            await page.GetByTestId("buttonSaveAndStay").ClickAsync();
            _logger.LogInformation(
                "InventoryAlign: {ProductCode} -> {TargetAmount} ({FreeAmount} free / {ReservedAmount} reserved -> {SetAmount})",
                request.ProductCode, request.TargetAmount, freeAmount, reservedAmount, setAmount);
        }
        else
        {
            _logger.LogInformation(
                "DRY RUN: InventoryAlign: {ProductCode} -> {TargetAmount} ({FreeAmount} free / {ReservedAmount} reserved -> {SetAmount})",
                request.ProductCode, request.TargetAmount, freeAmount, reservedAmount, setAmount);
        }


        // Zavření prohlížeče
        await browser.CloseAsync();

        return new StockTakingRecord()
        {
            Date = _timeProvider.GetUtcNow().DateTime,
            Code = request.ProductCode,
            AmountNew = (double)request.TargetAmount, // TODO Convert to decimal
            AmountOld = freeAmount + reservedAmount
        };
    }
}