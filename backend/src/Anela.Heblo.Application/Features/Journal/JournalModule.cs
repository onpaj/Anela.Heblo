using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Journal
{
    public static class JournalModule
    {
        public static IServiceCollection AddJournalModule(this IServiceCollection services)
        {
            // MediatR handlers are automatically registered by MediatR scan

            return services;
        }
    }
}