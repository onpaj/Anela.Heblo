using System.Globalization;
using System.Text;
using Anela.Heblo.Adapters.Flexi.Price;
using Anela.Heblo.Application.Domain.Catalog.Price;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Shoptet;

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

        priceList.ForEach(f =>
        {
            f.OriginalPrice = f.Price;
            f.OriginalPurchasePrice = f.PurchasePrice;
        });
        
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
        using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";"}))
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
            Map(m => m.Code).Index(0);
            Map(m => m.PairCode).Index(1);
            Map(m => m.Name).Index(2);
            Map(m => m.Price).Convert(a => decimal.TryParse(a.Row.GetField(3).Replace(",", "."), out var result) ? result : null);
            Map(m => m.PurchasePrice).Convert(a => decimal.TryParse(a.Row.GetField(4).Replace(",", "."), out var result) ? result : null);
        }
    }
    
    private class ProductPriceExportMap : ClassMap<ProductPriceEshop>
    {
        public ProductPriceExportMap()
        {
            Map(m => m.Code).Index(0);
            Map(m => m.PairCode).Index(1);
            Map(m => m.PurchasePrice).Index(2);
            Map(m => m.Name).Ignore();
            Map(m => m.Price).Ignore();
            Map(m => m.OriginalPrice).Ignore();
            Map(m => m.OriginalPurchasePrice).Ignore();
        }
    }
}

