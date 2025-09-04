namespace Anela.Heblo.Application.Common;

public interface IBackgroundServiceReadinessTracker
{
    void ReportInitialLoadCompleted<TService>() where TService : class;
    bool IsServiceReady<TService>() where TService : class;
    bool AreAllServicesReady();
    IReadOnlyDictionary<string, bool> GetServiceStatuses();
}