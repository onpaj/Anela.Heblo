using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Contracts;
using Anela.Heblo.Application.Features.Reporting.Data;

namespace Anela.Heblo.Application.Features.Reporting.UseCases;

internal static class ReportDatasetMapper
{
    public static ReportFreshnessDto ToDto(ReportFreshness f) => new() { LastSyncAt = f.LastSyncAt, Status = f.Status };

    public static ReportColumnDto ToDto(ReportColumn c) => new() { Name = c.Name, Type = c.Type.ToString(), Description = c.Description };

    public static ReportDatasetSummaryDto ToSummary(ReportDataset d, ReportFreshness f) => new()
    {
        Id = d.Id,
        Source = d.Source.ToString(),
        SourceLabel = ReportSources.LabelFor(d.Source),
        Description = d.Description,
        Grain = d.Grain,
        Freshness = ToDto(f),
    };

    public static ReportDatasetDetailDto ToDetail(ReportDataset d, ReportFreshness f) => new()
    {
        Id = d.Id,
        Source = d.Source.ToString(),
        SourceLabel = ReportSources.LabelFor(d.Source),
        Description = d.Description,
        Grain = d.Grain,
        Freshness = ToDto(f),
        PeriodColumn = d.PeriodColumn,
        Columns = d.Columns.Select(ToDto).ToList(),
        DefaultSort = d.DefaultSort.Select(s => $"{s.Column} {(s.Descending ? "desc" : "asc")}").ToList(),
        Caveats = d.Caveats.ToList(),
    };
}
