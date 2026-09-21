namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

/// <summary>Process-wide "a refresh or recompute is running" flag shared by the scheduled job, the recompute job and the API guard.</summary>
public sealed class MarketingPerformanceRunGuard
{
    private int _running;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public bool TryBegin() => Interlocked.CompareExchange(ref _running, 1, 0) == 0;

    public void End() => Volatile.Write(ref _running, 0);
}
