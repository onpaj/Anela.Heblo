using Anela.Heblo.Application.Domain.Catalog.ConsumedMaterials;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Client.Clients.UserQueries;
using Rem.FlexiBeeSDK.Client.ResultFilters;

namespace Anela.Heblo.Adapters.Flexi.Materials;

public class FlexiConsumedMaterialsQueryClient : UserQueryClient<ConsumedMaterialsFlexiDto>, IConsumedMaterialsClient
{
    private readonly IMapper _mapper;

    public FlexiConsumedMaterialsQueryClient(FlexiBeeSettings connection, IHttpClientFactory httpClientFactory, IResultHandler resultHandler, ILogger<ReceivedInvoiceClient> logger, IMapper mapper)
        : base(connection, httpClientFactory, resultHandler, logger)
    {
        _mapper = mapper;
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

    public async Task<IReadOnlyList<ConsumedMaterialRecord>> GetConsumedAsync(DateTime dateFrom, DateTime dateTo, int limit = 0, CancellationToken cancellationToken = default)
    {
        var dtos = await GetAsync(dateFrom, dateTo, limit, cancellationToken);

        return _mapper.Map<IReadOnlyList<ConsumedMaterialRecord>>(dtos);
    }
}