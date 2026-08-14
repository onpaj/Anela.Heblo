namespace Anela.Heblo.Application.Features.Attendance.Overtime;

public enum OvertimeActivityCategory
{
    Work,
    Break,
    Vacation,
    Sick,
    Doctor,
    Ocr,
    CompTime,
    Other
}

public class OvertimeOptions
{
    public const string ConfigKey = "Overtime";

    /// <summary>Logeto activity name → category name (Vacation/Sick/Doctor/Ocr/CompTime).
    /// Activities with Logeto Type=Work/Break need no mapping; unmapped non-Work activities
    /// fall into Other (not credited, surfaced as a warning).</summary>
    public Dictionary<string, string> ActivityCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>SharePoint drive for the generated report; empty = publishing disabled.</summary>
    public string ExportDriveId { get; set; } = string.Empty;

    /// <summary>Folder path inside the drive, e.g. "Provoz/Mzdy". Empty = drive root.</summary>
    public string ExportFolderPath { get; set; } = string.Empty;

    public string ExportFileName { get; set; } = "Evidence-prescasu.xlsx";
}
