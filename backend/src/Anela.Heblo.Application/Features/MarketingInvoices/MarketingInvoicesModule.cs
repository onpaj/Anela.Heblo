using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Anela.Heblo.Persistence.Features.MarketingInvoices;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.MarketingInvoices;

public static class MarketingInvoicesModule
{
    public static IServiceCollection AddMarketingInvoicesModule(this IServiceCollection services)
    {
        services.AddScoped<IImportedMarketingTransactionRepository, ImportedMarketingTransactionRepository>();

        // Register null implementation as default; consuming applications should override with their own
        services.AddScoped<IMarketingTransactionSource, NullMarketingTransactionSource>();

        services.AddScoped<MarketingInvoiceImportService>();

        return services;
    }
}
