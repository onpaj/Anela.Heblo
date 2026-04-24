using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Persistence.Marketing;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Marketing
{
    public static class MarketingModule
    {
        public static IServiceCollection AddMarketingModule(this IServiceCollection services)
        {
            services.AddScoped<IMarketingActionRepository, MarketingActionRepository>();
            // MediatR handlers are auto-registered by assembly scan
            return services;
        }
    }
}
