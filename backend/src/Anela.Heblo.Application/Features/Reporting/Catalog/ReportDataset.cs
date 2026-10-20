namespace Anela.Heblo.Application.Features.Reporting.Catalog;

public sealed class ReportDataset
{
    public required string Id { get; init; }
    public required ReportSource Source { get; init; }
    public required string Schema { get; init; }
    public required string View { get; init; }
    public required string Description { get; init; }
    public required string Grain { get; init; }
    public required string PeriodColumn { get; init; }
    public required IReadOnlyList<ReportColumn> Columns { get; init; }
    public required IReadOnlyList<string> KeyColumns { get; init; }
    public required IReadOnlyList<(string Column, bool Descending)> DefaultSort { get; init; }
    public IReadOnlyList<string> Caveats { get; init; } = [];

    public ReportColumn? FindColumn(string name) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
