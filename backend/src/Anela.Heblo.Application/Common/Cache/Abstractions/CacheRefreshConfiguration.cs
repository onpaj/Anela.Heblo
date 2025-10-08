namespace Anela.Heblo.Application.Common.Cache.Abstractions;

public class CacheRefreshConfiguration : ICacheRefreshConfiguration
{
    public string Name { get; set; } = string.Empty;
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan InitialDelay { get; set; } = TimeSpan.Zero;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public string[] Dependencies { get; set; } = Array.Empty<string>();
    public RetryPolicy RetryPolicy { get; set; } = new();
    public CacheFailureMode FailureMode { get; set; } = CacheFailureMode.KeepStale;
}