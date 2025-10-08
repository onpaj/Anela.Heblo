namespace Anela.Heblo.Application.Common.Cache.Abstractions;

public interface ICacheRegistrationBuilder
{
    ICacheRegistrationBuilder Register<TSource, TData>(
        string name,
        Func<IServiceProvider, TSource> dataSourceFactory,
        Func<TSource, CancellationToken, Task<TData>> refreshMethod,
        CacheRefreshConfiguration configuration)
        where TData : class;

    ICacheRegistrationBuilder Register<TSource, TData>(
        string name,
        Func<TSource, CancellationToken, Task<TData>> refreshMethod,
        CacheRefreshConfiguration configuration)
        where TSource : class
        where TData : class;
}