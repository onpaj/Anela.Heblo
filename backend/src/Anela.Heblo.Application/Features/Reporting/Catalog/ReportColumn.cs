namespace Anela.Heblo.Application.Features.Reporting.Catalog;

public sealed class ReportColumn
{
    public ReportColumn(string name, ReportColumnType type, string description)
    {
        Name = name;
        Type = type;
        Description = description;
    }

    public string Name { get; }
    public ReportColumnType Type { get; }
    public string Description { get; }

    public static ReportColumn Date(string name, string description) => new(name, ReportColumnType.Date, description);
    public static ReportColumn Text(string name, string description) => new(name, ReportColumnType.Text, description);
    public static ReportColumn Int(string name, string description) => new(name, ReportColumnType.Integer, description);
    public static ReportColumn Dec(string name, string description) => new(name, ReportColumnType.Decimal, description);
    public static ReportColumn Bool(string name, string description) => new(name, ReportColumnType.Boolean, description);
}
