namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

public interface IOvertimeReportPublisher
{
    bool IsConfigured { get; }

    /// <summary>Uploads (overwrites) the workbook to the configured SharePoint folder. Throws on failure.</summary>
    Task PublishAsync(byte[] content, string fileName, CancellationToken cancellationToken);
}
