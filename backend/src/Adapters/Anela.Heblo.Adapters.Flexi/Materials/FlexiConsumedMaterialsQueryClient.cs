using System.Globalization;
using Anela.Heblo.ConsumedMaterials;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Client.Clients.UserQueries;
using Rem.FlexiBeeSDK.Client.ResultFilters;

namespace Anela.Heblo.Adapters.Flexi.Materials;

public class FlexiConsumedMaterialsQueryClient : UserQueryClient<ConsumedMaterialsFlexiDto>, IConsumedMaterialsClient
{
    public FlexiConsumedMaterialsQueryClient(FlexiBeeSettings connection, IHttpClientFactory httpClientFactory, IResultHandler resultHandler, ILogger<ReceivedInvoiceClient> logger) 
        : base(connection, httpClientFactory, resultHandler, logger)
    {
    }

    protected override int QueryId => 21;

    public Task<IList<ConsumedMaterialsFlexiDto>> GetAsync(DateTime dateFrom, DateTime dateTo, int limit = 0,
        CancellationToken cancellationToken = default(CancellationToken)) =>
        GetAsync(new Dictionary<string, string>()
        {
            { "DATUM_OD", dateFrom.ToString("yyyy-MM-dd") },
            { "DATUM_DO", dateTo.ToString("yyyy-MM-dd") },
            { LimitParamName, limit.ToString() }
        }, cancellationToken);

    public async Task<IReadOnlyList<ConsumedMaterialHistory>> GetConsumedAsync(DateTime dateFrom, DateTime dateTo, int limit = 0, CancellationToken cancellationToken = default)
    {
        var dtos = await GetAsync(dateFrom, dateTo, limit, cancellationToken);

        return dtos.Select(s => new ConsumedMaterialHistory
        {
            ProductCode = s.ProductCode,
            ProductName = s.ProductName,
            Amount = s.Amount,
            Date = DateTime.Parse(s.Date, CultureInfo.InvariantCulture),
        }).ToList();
    }
}