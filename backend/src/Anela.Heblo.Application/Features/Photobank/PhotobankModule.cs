using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence.Photobank;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Photobank
{
    public static class PhotobankModule
    {
        public static IServiceCollection AddPhotobankModule(this IServiceCollection services)
        {
            services.AddScoped<IPhotobankRepository, PhotobankRepository>();
            // MediatR handlers are auto-registered by assembly scan
            return services;
        }
    }
}
