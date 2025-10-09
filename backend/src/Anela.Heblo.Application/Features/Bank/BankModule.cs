using Anela.Heblo.Application.Features.Bank.DashboardTiles;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Bank;

public static class BankModule
{
    public static IServiceCollection AddBankModule(this IServiceCollection services)
    {
        services.AddScoped<IBankStatementImportRepository, BankStatementImportRepository>();

        // Register dashboard tiles
        services.RegisterTile<BankStatementImportStatisticsTile>();

        return services;
    }
}