namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

/// <summary>
/// No-op implementation of IOvertimeReportPublisher used when mock authentication is active
/// or BypassJwtValidation is set. Reports as not configured so CloseMonthHandler skips
/// publishing instead of requiring Azure AD token acquisition.
/// </summary>
public sealed class NoOpOvertimeReportPublisher : IOvertimeReportPublisher
{
    public bool IsConfigured => false;

    public Task PublishAsync(byte[] content, string fileName, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Overtime report publishing is not available (mock auth active).");
}
