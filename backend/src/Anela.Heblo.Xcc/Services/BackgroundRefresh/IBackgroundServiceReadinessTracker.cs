namespace Anela.Heblo.Xcc.Services.BackgroundRefresh;

public interface IBackgroundServiceReadinessTracker
{
    void ReportInitialLoadCompleted<TService>() where TService : class;
    bool IsServiceReady<TService>() where TService : class;
    bool AreAllServicesReady();
    IReadOnlyDictionary<string, bool> GetServiceStatuses();

    // Tier-based hydration tracking
    void ReportHydrationStarted();
    void ReportHydrationCompleted();
    void ReportHydrationFailed(string? reason = null);
    IReadOnlyDictionary<string, object> GetHydrationDetails();
}