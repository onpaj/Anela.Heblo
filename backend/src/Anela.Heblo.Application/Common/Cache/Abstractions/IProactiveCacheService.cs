namespace Anela.Heblo.Application.Common.Cache.Abstractions;

public enum CacheStatus
{
    NotLoaded,
    Loading,
    Ready,
    Stale,
    Failed
}

public interface IProactiveCacheService<T> where T : class
{
    T? GetCurrent();
    DateTime? LastRefreshTime { get; }
    bool IsReady { get; }
    CacheStatus Status { get; }
    Task<bool> ForceRefreshAsync(CancellationToken ct = default);
}