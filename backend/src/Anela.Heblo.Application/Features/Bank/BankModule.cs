using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Bank;

public static class BankModule
{
    public static IServiceCollection AddBankModule(this IServiceCollection services)
    {
        // Register AutoMapper profile
        services.AddAutoMapper(typeof(BankMappingProfile));

        return services;
    }
}