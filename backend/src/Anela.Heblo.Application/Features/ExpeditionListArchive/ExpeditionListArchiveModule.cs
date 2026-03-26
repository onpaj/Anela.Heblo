using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive;

public static class ExpeditionListArchiveModule
{
    public static IServiceCollection AddExpeditionListArchiveModule(this IServiceCollection services)
    {
        // ReprintExpeditionListHandler needs the keyed "cups" IPrintQueueSink when available
        // (production/staging). In environments where only the non-keyed sink is registered
        // (e.g. FileSystem in development/test), we fall back to the non-keyed registration.
        // This explicit factory overrides MediatR's auto-registration so the correct sink is injected.
        services.AddTransient<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>(provider =>
        {
            var blobStorage = provider.GetRequiredService<IBlobStorageService>();
            var cupsSink = provider.GetKeyedService<IPrintQueueSink>("cups")
                ?? provider.GetRequiredService<IPrintQueueSink>();
            return new ReprintExpeditionListHandler(blobStorage, cupsSink);
        });

        return services;
    }
}
