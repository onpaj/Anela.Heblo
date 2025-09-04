namespace Anela.Heblo.Application.Common;

public interface IBackgroundServiceReadinessTracker
{
    void ReportInitialLoadCompleted(string serviceName);
    bool IsServiceReady(string serviceName);
    bool AreAllServicesReady();
    IReadOnlyDictionary<string, bool> GetServiceStatuses();
}