
using Anela.Heblo.Adapters.Shoptet.Playwright;
using Microsoft.Extensions.Configuration;

Console.WriteLine("Hello, World!");

var conf = new ConfigurationBuilder()
    .AddUserSecrets<ShoptetPlaywrightInvoiceSource>()
    .Build();

var options = conf.GetSection("Shoptet.Playwright").Get<PlaywrightSourceOptions>();

foreach (var f in Directory.EnumerateFiles(options.PdfTmpFolder))
    File.Delete(f);
