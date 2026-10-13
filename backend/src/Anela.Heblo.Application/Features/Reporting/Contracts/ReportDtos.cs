namespace Anela.Heblo.Application.Features.Reporting.Contracts;

public class ReportFreshnessDto
{
    public DateTimeOffset? LastSyncAt { get; set; }
    public string Status { get; set; } = "UNKNOWN";
}

public class ReportColumnDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class ReportDatasetSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Grain { get; set; } = string.Empty;
    public ReportFreshnessDto Freshness { get; set; } = new();
}

public class ReportDatasetDetailDto : ReportDatasetSummaryDto
{
    public string PeriodColumn { get; set; } = string.Empty;
    public List<ReportColumnDto> Columns { get; set; } = [];
    public List<string> DefaultSort { get; set; } = [];
    public List<string> Caveats { get; set; } = [];
}

public class ReportFilterDto
{
    public string Column { get; set; } = string.Empty;
    public string Op { get; set; } = string.Empty;
    public string? Value { get; set; }
    public List<string>? Values { get; set; }
}

public class ReportSortDto
{
    public string Column { get; set; } = string.Empty;
    public string Direction { get; set; } = "asc";
}
