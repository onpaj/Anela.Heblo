using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Xcc.Services.BackgroundRefresh;

public static class BackgroundRefreshExtensions
{
    public static IServiceCollection AddBackgroundRefresh(this IServiceCollection services)
    {
        services.AddSingleton<BackgroundRefreshTaskRegistry>();
        services.AddSingleton<IBackgroundRefreshTaskRegistry>(provider => 
            provider.GetRequiredService<BackgroundRefreshTaskRegistry>());
        services.AddHostedService<BackgroundRefreshSchedulerService>();

        return services;
    }

    public static IServiceCollection RegisterRefreshTask(
        this IServiceCollection services,
        string ownerName,
        string methodName,
        Func<IServiceProvider, CancellationToken, Task> refreshMethod,
        RefreshTaskConfiguration configuration)
    {
        // Register task immediately by configuring the singleton registry
        services.Configure<BackgroundRefreshTaskRegistrySetup>(setup =>
        {
            setup.TaskRegistrations.Add(new TaskRegistrationInfo
            {
                TaskId = GetTaskId(ownerName, methodName),
                RefreshMethod = refreshMethod,
                Configuration = configuration
            });
        });

        return services;
    }

    public static IServiceCollection RegisterRefreshTask(
        this IServiceCollection services,
        string ownerName,
        string methodName,
        Func<IServiceProvider, CancellationToken, Task> refreshMethod)
    {

        // Register task immediately by configuring the singleton registry
        services.Configure<BackgroundRefreshTaskRegistrySetup>(setup =>
        {
            setup.TaskRegistrations.Add(new TaskRegistrationInfo
            {
                TaskId = GetTaskId(ownerName, methodName),
                RefreshMethod = refreshMethod,
            });
        });

        return services;
    }

    private static string GetTaskId(string ownerType, string methodName)
    {
        return $"{ownerType}.{methodName}";
    }

    public static IServiceCollection RegisterRefreshTask<TOwner>(
        this IServiceCollection services,
        string methodName,
        Func<IServiceProvider, CancellationToken, Task> refreshMethod,
        RefreshTaskConfiguration configuration)
        where TOwner : class
    {
        var ownerType = typeof(TOwner).Name;
        return services.RegisterRefreshTask(ownerType, methodName, refreshMethod, configuration);
    }

    public static IServiceCollection RegisterRefreshTask<TOwner>(
        this IServiceCollection services,
        string methodName,
        Func<IServiceProvider, CancellationToken, Task> refreshMethod)
        where TOwner : class
    {
        var ownerType = typeof(TOwner).Name;
        return services.RegisterRefreshTask(ownerType, methodName, refreshMethod);
    }
    
    public static IServiceCollection RegisterRefreshTask<TOwner>(
        this IServiceCollection services,
        string methodName,
        Action<TOwner, CancellationToken> refreshMethod)
        where TOwner : class
    {
        var ownerType = typeof(TOwner).Name;
        var wrappedMethod = CreateWrappedMethod(refreshMethod);
        return services.RegisterRefreshTask(ownerType, methodName, wrappedMethod);
    }
    
    public static IServiceCollection RegisterRefreshTask<TOwner>(
        this IServiceCollection services,
        string methodName,
        Func<TOwner, CancellationToken, Task> refreshMethod)
        where TOwner : class
    {
        var ownerType = typeof(TOwner).Name;
        var wrappedMethod = CreateWrappedMethod(refreshMethod);
        return services.RegisterRefreshTask(ownerType, methodName, wrappedMethod);
    }
    

    private static Func<IServiceProvider, CancellationToken, Task> CreateWrappedMethod<TOwner>(
        Action<TOwner, CancellationToken> refreshMethod)
        where TOwner : class
    {
        return async (serviceProvider, cancellationToken) =>
        {
            using var scope = serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TOwner>();
            refreshMethod(service, cancellationToken);
            await Task.CompletedTask;
        };
    }
    
  

    private static Func<IServiceProvider, CancellationToken, Task> CreateWrappedMethod<TOwner>(
        Func<TOwner, CancellationToken, Task> refreshMethod)
        where TOwner : class
    {
        return async (serviceProvider, cancellationToken) =>
        {
            using var scope = serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TOwner>();
            await refreshMethod(service, cancellationToken);
        };
    }
}