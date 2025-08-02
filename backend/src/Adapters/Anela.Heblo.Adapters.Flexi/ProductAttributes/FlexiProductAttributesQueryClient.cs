using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Client.Clients.UserQueries;
using Rem.FlexiBeeSDK.Client.ResultFilters;

namespace Anela.Heblo.Adapters.Flexi.ProductAttributes;

public class FlexiProductAttributesQueryClient : UserQueryClient<ProductAttributesFlexiDto>, ICatalogAttributesClient
{
    private readonly ISeasonalDataParser _seasonalDataParser;
    private const int OverstockAttributeId = 80;
    private const int StockMinAttributeId = 81;
    private const int BatchSizeAttributeId = 82;
    private const int SeasonalAttributeId = 84;
    private const int MmqAttributeId = 85;

    public FlexiProductAttributesQueryClient(
        FlexiBeeSettings connection,
        IHttpClientFactory httpClientFactory,
        IResultHandler resultHandler,
        ISeasonalDataParser seasonalDataParser,
        ILogger<ReceivedInvoiceClient> logger
    )
        : base(connection, httpClientFactory, resultHandler, logger)
    {
        _seasonalDataParser = seasonalDataParser;
    }

    protected override int QueryId => 38;

    public Task<IList<ProductAttributesFlexiDto>> GetAsync(int limit = 0, CancellationToken cancellationToken = default) =>
        GetAsync(new Dictionary<string, string>() { { LimitParamName, limit.ToString() } }, cancellationToken);


    public async Task<IList<CatalogAttributes>> GetAttributesAsync(int limit = 0, CancellationToken cancellationToken = default)
    {
        var data = (await GetAsync(limit, cancellationToken));

        return data.GroupBy(g => new { g.ProductId, g.ProductCode, g.ProductType }, (key, values) => new CatalogAttributes()
        {
            ProductId = key.ProductId,
            ProductCode = key.ProductCode,
            ProductType = ParseProductType(key.ProductType),
            OptimalStockDays = StrToIntDef(values.FirstOrDefault(w => w.AttributeId == OverstockAttributeId)?.Value, 0),
            StockMin = StrToIntDef(values.FirstOrDefault(w => w.AttributeId == StockMinAttributeId)?.Value, 0),
            BatchSize = StrToIntDef(values.FirstOrDefault(w => w.AttributeId == BatchSizeAttributeId)?.Value, 0),
            MinimalManufactureQuantity = StrToIntDef(values.FirstOrDefault(w => w.AttributeId == MmqAttributeId)?.Value, 0),
            SeasonMonthsArray = _seasonalDataParser.GetSeasonalMonths(values.FirstOrDefault(w => w.AttributeId == SeasonalAttributeId)?.Value),
        }).ToList();
    }

    private static int StrToIntDef(string? s, int @default)
    {
        int number;
        if (int.TryParse(s, out number))
            return number;
        return @default;
    }




    private static ProductType ParseProductType(string productType)
    {
        return productType switch
        {
            ProductAttributesFlexiDto.ProductType_Product => ProductType.Product,
            ProductAttributesFlexiDto.ProductType_Material => ProductType.Material,

            _ => ProductType.UNDEFINED,
        };
    }
}