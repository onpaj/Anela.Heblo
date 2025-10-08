namespace Anela.Heblo.Application.Common.Cache.Abstractions;

public enum CacheStatus
{
    NotLoaded,
    Loading,
    Ready,
    Stale,
    Failed
}