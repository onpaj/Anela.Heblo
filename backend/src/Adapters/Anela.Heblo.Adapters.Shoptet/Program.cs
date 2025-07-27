
using Anela.Heblo.Adapters.Shoptet.Playwright;
using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Anela.Heblo.Logistics.Picking.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

Console.WriteLine("Hello, World!");

var conf = new ConfigurationBuilder()
    .AddUserSecrets<ShoptetPlaywrightInvoiceSource>()
    .Build();

var log = LoggerFactory.Create(b =>
{
    b.SetMinimumLevel(LogLevel.Debug);
}).CreateLogger<PrintPickingListScenario>();

var options = conf.GetSection("Shoptet.Playwright").Get<PlaywrightSourceOptions>();

//var source = new IssuedInvoiceExportScenario(options, NullLogger<ShoptetPlaywrightSource>.Instance);
//await source.RunAsync(new IssuedInvoiceSourceQuery() { InvoiceId = "123008354"});

foreach(var f in Directory.EnumerateFiles(options.PdfTmpFolder))
    File.Delete(f);

var source = new ShoptetPlaywrightExpeditionListSource(new PrintPickingListScenario(options, log));
await source.CreatePickingList(new PrintPickingListRequest(), CancellationToken.None);
