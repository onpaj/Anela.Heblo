using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Bank.Infrastructure;
using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;
using Anela.Heblo.Application.Features.Bank.Validators;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Persistence.Features.Bank;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Bank;

public static class BankModule
{
    public static IServiceCollection AddBankModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<IBankClientFactory, BankClientFactory>();
        services.Configure<BankAccountSettings>(configuration.GetSection(BankAccountSettings.ConfigurationKey));

        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IBankStatementImportRepository, BankStatementImportRepository>();
        services.AddScoped<IBankImportStateRepository, BankImportStateRepository>();
        services.Configure<BankImportWatermarkOptions>(
            configuration.GetSection(BankImportWatermarkOptions.SectionName));

        services.AddScoped<IValidator<GetBankStatementListRequest>, GetBankStatementListRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetBankStatementListRequest, GetBankStatementListResponse>,
            ValidationBehavior<GetBankStatementListRequest, GetBankStatementListResponse>>();

        // Cross-module contract: Bank implements Analytics' IBankStatementStatisticsSource
        // via an adapter. DI registration owned by provider (Bank), not consumer (Analytics).
        // Scoped because the adapter wraps ApplicationDbContext (also Scoped).
        services.AddScoped<IBankStatementStatisticsSource, BankStatementStatisticsSourceAdapter>();

        return services;
    }
}
