namespace Anela.Heblo.Application.Common.Cache.Abstractions;

public interface ICacheRefreshConfiguration
{
    string Name { get; }
    TimeSpan RefreshInterval { get; }
    TimeSpan InitialDelay { get; }
    bool Enabled { get; }
    int Priority { get; }
    string[] Dependencies { get; }
    RetryPolicy RetryPolicy { get; }
    CacheFailureMode FailureMode { get; }
}