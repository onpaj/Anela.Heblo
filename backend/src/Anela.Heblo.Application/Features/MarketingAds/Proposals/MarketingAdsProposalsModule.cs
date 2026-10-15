using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Autonomy;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.MarketingAds;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals;

/// <summary>The proposal &amp; approval layer (PR C3). Called from <see cref="MarketingAdsModule.AddMarketingAdsModule"/> (spec 12.1).</summary>
public static class MarketingAdsProposalsModule
{
    public static IServiceCollection AddMarketingAdsProposalsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // No Validate/ValidateOnStart: a bad setting must fail a run (AdProposalOptions.EnsureValid), never
        // app startup or recurring-job discovery.
        services.Configure<AdProposalOptions>(configuration.GetSection(AdProposalOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IAdProposalRepository, AdProposalRepository>();
        services.AddScoped<IAdGovernanceRepository, AdGovernanceRepository>();
        services.AddScoped<IAgentRunRepository, AgentRunRepository>();
        services.AddScoped<IAdAuditLog, AdAuditLog>();

        services.AddSingleton<IAdLimitsEvaluator, AdLimitsEvaluator>();
        services.AddScoped<IAdLimitsContextBuilder, AdLimitsContextBuilder>();
        // AdsDbContext is registered by C1 only when AdsDatabase:ConnectionString is really configured.
        services.AddScoped<IAdsReadModel>(sp => sp.GetService<AdsDbContext>() is { } db
            ? new AdsDbReadModel(db)
            : new UnavailableAdsReadModel());
        services.AddScoped<IAdAutonomyService, AdAutonomyService>();

        // The claims-based approval channel resolver (spec 7.4, PR C4).
        services.AddApprovalChannel(configuration);

        services.AddScoped<IAdProposalExecutionEnqueuer, HangfireAdProposalExecutionEnqueuer>();
        services.AddScoped<AdProposalExecutionJob>();

        // MediatR handlers and the IRecurringJob (AdProposalExpiryJob) are discovered by assembly scan.
        return services;
    }
}
