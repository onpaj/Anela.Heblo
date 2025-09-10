using System.Globalization;
using System.Text;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Xcc.Audit;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Shoptet.Stock;

public class ShoptetStockClient : IEshopStockClient
{
    private readonly HttpClient _client;
    private readonly IOptions<ShoptetStockClientOptions> _options;
    private readonly IDataLoadAuditService _auditService;

    public ShoptetStockClient(
        HttpClient client,
        IOptions<ShoptetStockClientOptions> options,
        IDataLoadAuditService auditService)
    {
        _client = client;
        _options = options;
        _auditService = auditService;
    }

    public async Task<List<EshopStock>> ListAsync(CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var parameters = new Dictionary<string, object>
        {
            ["url"] = _options.Value.Url,
            ["encoding"] = "windows-1250",
            ["delimiter"] = ";"
        };

        try
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

            var duration = DateTime.UtcNow - startTime;
            await _auditService.LogDataLoadAsync(
                dataType: "Stock",
                source: "Shoptet E-shop",
                recordCount: stockDataList.Count,
                success: true,
                parameters: parameters,
                duration: duration);

            return stockDataList;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            await _auditService.LogDataLoadAsync(
                dataType: "Stock",
                source: "Shoptet E-shop",
                recordCount: 0,
                success: false,
                parameters: parameters,
                errorMessage: ex.Message,
                duration: duration);
            throw;
        }
    }

    private class StockDataMap : ClassMap<EshopStock>
    {
        public StockDataMap()
        {
            Map(m => m.Code).Index(0);
            Map(m => m.PairCode).Index(1);
            Map(m => m.Name).Index(2);
            Map(m => m.Stock).Index(25)
                .TypeConverterOption
                .NullValues(string.Empty)
                .Default(0m);
            Map(m => m.NameSuffix).Index(15);
            Map(m => m.Location).Index(26);
            Map(m => m.DefaultImage).Index(3);
            Map(m => m.Image).Index(4);
            Map(m => m.Weight).Index(27);
            Map(m => m.Height).Index(28);
            Map(m => m.Depth).Index(29);
            Map(m => m.Width).Index(30);
            Map(m => m.AtypicalShipping).Index(31);
        }
    }
}