using Anela.Heblo.Domain.Features.Bank;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Bank;

public static class BankModule
{
    public static IServiceCollection AddBankModule(this IServiceCollection services)
    {
        services.AddScoped<IBankStatementImportRepository, BankStatementImportRepository>();

        return services;
    }
}