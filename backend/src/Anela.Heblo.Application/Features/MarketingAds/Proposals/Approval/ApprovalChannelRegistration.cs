using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;

public static class ApprovalChannelRegistration
{
    /// <summary>Binds <see cref="ApprovalChannelOptions"/> and replaces the deny-all placeholder
    /// resolver registered by the proposals slice with the real one.</summary>
    public static IServiceCollection AddApprovalChannel(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApprovalChannelOptions>()
            .Bind(configuration.GetSection(ApprovalChannelOptions.SectionName))
            .Configure(options => options.ApiClientId = configuration[ApprovalChannelOptions.ApiClientIdConfigKey]);

        services.Replace(ServiceDescriptor.Scoped<IApprovalChannelResolver, ApprovalChannelResolver>());
        return services;
    }
}
