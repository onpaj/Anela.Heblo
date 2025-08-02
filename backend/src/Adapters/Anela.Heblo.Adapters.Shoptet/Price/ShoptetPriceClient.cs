using System.Globalization;
using System.Text;
using Anela.Heblo.Domain.Features.Catalog.Price;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Shoptet.Price;

public class ShoptetPriceClient : IProductPriceEshopClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<ProductPriceOptions> _options;

    public ShoptetPriceClient(HttpClient httpClient, IOptions<ProductPriceOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<IEnumerable<ProductPriceEshop>> GetAllAsync(CancellationToken cancellationToken)
    {
        List<ProductPriceEshop> priceList = new List<ProductPriceEshop>();

        using (HttpResponseMessage response = await _httpClient.GetAsync(_options.Value.ProductExportUrl, cancellationToken))
        using (Stream csvStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        using (StreamReader reader = new StreamReader(csvStream, Encoding.GetEncoding("windows-1250")))
        using (CsvReader csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
        {
            csv.Context.RegisterClassMap<ProductPriceImportMap>();
            priceList = csv.GetRecords<ProductPriceEshop>().ToList();
        }

        // Removed OriginalPrice and OriginalPurchasePrice assignments as these properties don't exist in simplified model

        return priceList;
    }

    public async Task<SetProductPricesResultDto> SetAllAsync(IEnumerable<ProductPriceEshop> destinationData, CancellationToken cancellationToken)
    {
        var csvPath = CreateCsv(destinationData);
        Console.WriteLine(csvPath);

        var result = new SetProductPricesResultDto()
        {
            FilePath = csvPath,
            Data = await File.ReadAllBytesAsync(csvPath, cancellationToken)
        };
        return result;
    }

    private string CreateCsv(IEnumerable<ProductPriceEshop> destinationData)
    {
        // Create a temporary file in the system's temporary folder
        var tempPath = Path.GetTempPath();
        var tempFile = Path.Combine(tempPath, "products.csv");
        using (var stream = File.Create(tempFile))
        using (var writer = new StreamWriter(stream, Encoding.UTF8))
        using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
        {
            csv.Context.RegisterClassMap<ProductPriceExportMap>();
            csv.WriteHeader<ProductPriceEshop>();
            csv.NextRecord();

            foreach (var product in destinationData)
            {
                csv.WriteRecord(product);
                csv.NextRecord();
            }
        }

        return tempFile;
    }

    private class ProductPriceImportMap : ClassMap<ProductPriceEshop>
    {
        public ProductPriceImportMap()
        {
            // Map ProductCode from first column
            Map(m => m.ProductCode).Index(0);

            // Map prices from columns 3 and 4 (skip PairCode and Name)
            Map(m => m.PriceWithVat).Convert(a =>
            {
                var fieldValue = a.Row.GetField(3);
                if (string.IsNullOrWhiteSpace(fieldValue))
                    return null;
                return decimal.TryParse(fieldValue.Replace(",", "."), NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? (decimal?)result : null;
            });
            Map(m => m.PurchasePrice).Convert(a =>
            {
                var fieldValue = a.Row.GetField(4);
                if (string.IsNullOrWhiteSpace(fieldValue))
                    return null;
                return decimal.TryParse(fieldValue.Replace(",", "."), NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? (decimal?)result : null;
            });
        }
    }

    private class ProductPriceExportMap : ClassMap<ProductPriceEshop>
    {
        public ProductPriceExportMap()
        {
            Map(m => m.ProductCode).Index(0);
            Map(m => m.PriceWithVat).Index(1);
            Map(m => m.PurchasePrice).Index(2);
        }
    }
}

