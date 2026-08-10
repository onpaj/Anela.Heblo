using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.ExportOvertimeReport;

public class ExportOvertimeReportResponse : BaseResponse
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
}
