using Anela.Heblo.Application.Common.Cache.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Common.Cache.Implementation;

public class CacheRegistrationBuilder : ICacheRegistrationBuilder
{
    private readonly IServiceCollection _services;
    private readonly List<Action<IServiceProvider, ProactiveCacheOrchestrator>> _registrationActions = new();

    public CacheRegistrationBuilder(IServiceCollection services)
    {
        _services = services;
    }

    public ICacheRegistrationBuilder Register<TSource, TData>(
        string name,
        Func<IServiceProvider, TSource> dataSourceFactory,
        Func<TSource, CancellationToken, Task<TData>> refreshMethod,
        CacheRefreshConfiguration configuration)
        where TData : class
    {
        configuration.Name = name;

        // Register the cache service
        _services.AddSingleton<IProactiveCacheService<TData>>(serviceProvider =>
        {
            var dataSource = dataSourceFactory(serviceProvider);
            var logger = serviceProvider.GetRequiredService<ILogger<ProactiveCacheDecorator<TSource, TData>>>();
            var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();

            return new ProactiveCacheDecorator<TSource, TData>(
                dataSource,
                refreshMethod,
                configuration,
                logger,
                timeProvider);
        });

        // Store registration action for the orchestrator
        _registrationActions.Add((serviceProvider, orchestrator) =>
        {
            var cacheService = serviceProvider.GetRequiredService<IProactiveCacheService<TData>>();
            var registration = new CacheRegistration
            {
                Name = name,
                Configuration = configuration,
                CacheService = cacheService,
                CacheServiceType = typeof(ProactiveCacheDecorator<TSource, TData>)
            };
            orchestrator.RegisterCache(registration);
        });

        return this;
    }

    public ICacheRegistrationBuilder Register<TSource, TData>(
        string name,
        Func<TSource, CancellationToken, Task<TData>> refreshMethod,
        CacheRefreshConfiguration configuration)
        where TSource : class
        where TData : class
    {
        return Register<TSource, TData>(
            name,
            serviceProvider => serviceProvider.GetRequiredService<TSource>(),
            refreshMethod,
            configuration);
    }

    internal void ConfigureOrchestrator(IServiceProvider serviceProvider, ProactiveCacheOrchestrator orchestrator)
    {
        foreach (var action in _registrationActions)
        {
            action(serviceProvider, orchestrator);
        }
    }
}