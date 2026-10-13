using System.Globalization;
using Anela.Heblo.Application.Features.Reporting.Catalog;
using NpgsqlTypes;

namespace Anela.Heblo.Application.Features.Reporting.Query;

public static class ReportValueParser
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy-MM"];

    public static NpgsqlDbType DbTypeFor(ReportColumnType type) => type switch
    {
        ReportColumnType.Date => NpgsqlDbType.Date,
        ReportColumnType.Text => NpgsqlDbType.Text,
        ReportColumnType.Integer => NpgsqlDbType.Bigint,
        ReportColumnType.Decimal => NpgsqlDbType.Numeric,
        ReportColumnType.Boolean => NpgsqlDbType.Boolean,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static object Parse(ReportColumn column, string raw) => column.Type switch
    {
        ReportColumnType.Text => raw,
        ReportColumnType.Date => ParseDate(column, raw),
        ReportColumnType.Integer => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
            ? l
            : throw Invalid(column, raw, "integer"),
        ReportColumnType.Decimal => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : throw Invalid(column, raw, "decimal number"),
        ReportColumnType.Boolean => ParseBool(raw) ?? throw Invalid(column, raw, "boolean (true or false)"),
        _ => throw new ArgumentOutOfRangeException(nameof(column), column.Type, null),
    };

    public static bool? ParseBool(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    private static DateOnly ParseDate(ReportColumn column, string raw) =>
        DateOnly.TryParseExact(raw.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw Invalid(column, raw, "date (yyyy-MM-dd or yyyy-MM)");

    private static ReportQueryValidationException Invalid(ReportColumn column, string raw, string expected) =>
        new($"Value '{raw}' for column '{column.Name}' is not a valid {expected}.");
}
