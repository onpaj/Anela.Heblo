using System.Globalization;
using System.Text;
using Anela.Heblo.Catalog.Stock;
using Anela.Heblo.Data;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Shoptet;

public class ShoptetStockClient : IEshopStockClient
{
    private readonly HttpClient _client;
    private readonly IOptions<ShoptetStockClientOptions> _options;
    private readonly ISynchronizationContext _synchronizationContext;

    public ShoptetStockClient(
        HttpClient client, 
        IOptions<ShoptetStockClientOptions> options,
        ISynchronizationContext synchronizationContext)
    {
        _client = client;
        _options = options;
        _synchronizationContext = synchronizationContext;
    }
    
    public async Task<List<EshopStock>> ListAsync(CancellationToken cancellationToken)
    {
        List<EshopStock> stockDataList = new List<EshopStock>();

        using (HttpResponseMessage response = await _client.GetAsync(_options.Value.Url, cancellationToken))
        using (Stream csvStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        using (StreamReader reader = new StreamReader(csvStream, Encoding.GetEncoding("windows-1250")))
        using (CsvReader csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
        {
            csv.Context.RegisterClassMap<StockDataMap>();
            stockDataList = csv.GetRecords<EshopStock>().ToList();
        }

        _synchronizationContext.Submit(new ProductShoptetStockSyncData(stockDataList));
        
        return stockDataList;
    }
    
    private class StockDataMap : ClassMap<EshopStock>
    {
        public StockDataMap()
        {
            Map(m => m.Code).Index(0);
            Map(m => m.PairCode).Index(1);
            Map(m => m.Name).Index(2);
            Map(m => m.Stock).Index(17)
                .TypeConverterOption
                .NullValues(string.Empty)
                .Default(0m);
            Map(m => m.NameSuffix).Index(7);
            Map(m => m.Location).Index(17);
        }
    }
}