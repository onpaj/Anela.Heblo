using System.Text;
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Npgsql;
using NpgsqlTypes;

namespace Anela.Heblo.Application.Features.Reporting.Query;

/// <summary>
/// Turns a structured query into parameterised SQL. Identifiers come only from the catalogue
/// (canonical names, double-quoted); every value is a typed parameter.
/// </summary>
public static class ReportSqlBuilder
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 1000;
    public const int MaxOffset = 100_000;
    public const int MaxFilters = 20;
    public const int MaxInValues = 100;
    public const int MaxSortColumns = 5;

    private static readonly string[] Operators = ["eq", "neq", "gt", "gte", "lt", "lte", "in", "between", "contains", "isNull"];

    public static ReportSqlCommand Build(ReportDataset dataset, ReportQuery query)
    {
        var limit = query.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaxLimit)
            throw new ReportQueryValidationException($"limit must be between 1 and {MaxLimit}.");
        var offset = query.Offset ?? 0;
        if (offset is < 0 or > MaxOffset)
            throw new ReportQueryValidationException($"offset must be between 0 and {MaxOffset}.");

        var output = ResolveColumns(dataset, query.Columns);
        var parameters = new List<NpgsqlParameter>();
        var where = BuildWhere(dataset, query.Filters, parameters);
        var orderBy = BuildOrderBy(dataset, query.Sort);

        parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = limit + 1 });
        parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Integer) { Value = offset });

        var sql = new StringBuilder()
            .Append("SELECT ").Append(string.Join(", ", output.Select(c => Quote(c.Name))))
            .Append(" FROM ").Append(Quote(dataset.Schema)).Append('.').Append(Quote(dataset.View));
        if (where.Length > 0)
            sql.Append(" WHERE ").Append(where);
        sql.Append(" ORDER BY ").Append(orderBy).Append(" LIMIT @limit OFFSET @offset");

        return new ReportSqlCommand
        {
            Sql = sql.ToString(),
            Parameters = parameters,
            OutputColumns = output,
            Limit = limit,
        };
    }

    private static IReadOnlyList<ReportColumn> ResolveColumns(ReportDataset dataset, IReadOnlyList<string>? requested)
    {
        if (requested is null)
            return dataset.Columns;
        if (requested.Count == 0)
            throw new ReportQueryValidationException("columns must not be empty; omit it to return all columns.");
        return requested.Select(name => RequireColumn(dataset, name)).ToList();
    }

    private static string BuildWhere(ReportDataset dataset, IReadOnlyList<ReportFilter> filters, List<NpgsqlParameter> parameters)
    {
        if (filters.Count > MaxFilters)
            throw new ReportQueryValidationException($"Use at most {MaxFilters} filters.");
        return string.Join(" AND ", filters.Select(f => BuildPredicate(dataset, f, parameters)));
    }

    private static string BuildPredicate(ReportDataset dataset, ReportFilter filter, List<NpgsqlParameter> parameters)
    {
        var column = RequireColumn(dataset, filter.Column);
        var op = Operators.FirstOrDefault(o => string.Equals(o, filter.Op, StringComparison.OrdinalIgnoreCase))
            ?? throw new ReportQueryValidationException(
                $"Unknown operator '{filter.Op}'. Allowed: {string.Join(", ", Operators)}.");
        var col = Quote(column.Name);

        switch (op)
        {
            case "isNull":
                var isNull = ReportValueParser.ParseBool(filter.Value)
                    ?? throw new ReportQueryValidationException("isNull requires 'value' true or false.");
                return isNull ? $"{col} IS NULL" : $"{col} IS NOT NULL";

            case "in":
                if (filter.Values is not { Count: >= 1 and <= MaxInValues })
                    throw new ReportQueryValidationException($"in requires 'values' with between 1 and {MaxInValues} items.");
                return $"{col} = ANY({AddArray(column, filter.Values, parameters)})";

            case "between":
                RequireOrderable(column, op);
                if (filter.Values is not { Count: 2 })
                    throw new ReportQueryValidationException("between requires 'values' with exactly 2 items.");
                return $"{col} BETWEEN {Add(column, filter.Values[0], parameters)} AND {Add(column, filter.Values[1], parameters)}";

            case "contains":
                if (column.Type != ReportColumnType.Text)
                    throw new ReportQueryValidationException($"contains is only allowed on Text columns; '{column.Name}' is {column.Type}.");
                var text = RequireValue(filter, op);
                return $"{col} ILIKE '%' || {AddRaw(NpgsqlDbType.Text, EscapeLike(text), parameters)} || '%' ESCAPE '\\'";

            default:
                if (op is "gt" or "gte" or "lt" or "lte")
                    RequireOrderable(column, op);
                var value = Add(column, RequireValue(filter, op), parameters);
                return op switch
                {
                    "eq" => $"{col} = {value}",
                    "neq" => $"{col} IS DISTINCT FROM {value}",
                    "gt" => $"{col} > {value}",
                    "gte" => $"{col} >= {value}",
                    "lt" => $"{col} < {value}",
                    _ => $"{col} <= {value}",
                };
        }
    }

    private static string BuildOrderBy(ReportDataset dataset, IReadOnlyList<ReportSort> sort)
    {
        if (sort.Count > MaxSortColumns)
            throw new ReportQueryValidationException($"Use at most {MaxSortColumns} sort entries.");

        var entries = sort.Count == 0
            ? dataset.DefaultSort.Select(s => (Column: dataset.FindColumn(s.Column)!, s.Descending)).ToList()
            : sort.Select(s => (Column: RequireColumn(dataset, s.Column), Descending: ParseDirection(s.Direction))).ToList();

        var used = entries.Select(e => e.Column.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var key in dataset.KeyColumns.Where(k => used.Add(k)))
            entries.Add((dataset.FindColumn(key)!, false));

        return string.Join(", ", entries.Select(e => $"{Quote(e.Column.Name)} {(e.Descending ? "DESC" : "ASC")} NULLS LAST"));
    }

    private static bool ParseDirection(string direction) => direction.Trim().ToLowerInvariant() switch
    {
        "asc" => false,
        "desc" => true,
        _ => throw new ReportQueryValidationException($"Sort direction '{direction}' must be asc or desc."),
    };

    private static ReportColumn RequireColumn(ReportDataset dataset, string name) =>
        dataset.FindColumn(name) ?? throw new ReportQueryValidationException(
            $"Unknown column '{name}' in dataset '{dataset.Id}'. Columns: {string.Join(", ", dataset.Columns.Select(c => c.Name))}.");

    private static void RequireOrderable(ReportColumn column, string op)
    {
        if (column.Type is ReportColumnType.Text or ReportColumnType.Boolean)
            throw new ReportQueryValidationException($"{op} is not allowed on {column.Type} column '{column.Name}'.");
    }

    private static string RequireValue(ReportFilter filter, string op) =>
        filter.Value ?? throw new ReportQueryValidationException($"{op} requires 'value'.");

    private static string Add(ReportColumn column, string raw, List<NpgsqlParameter> parameters) =>
        AddRaw(ReportValueParser.DbTypeFor(column.Type), ReportValueParser.Parse(column, raw), parameters);

    private static string AddArray(ReportColumn column, IReadOnlyList<string> raws, List<NpgsqlParameter> parameters)
    {
        var values = raws.Select(r => ReportValueParser.Parse(column, r)).ToArray();
        Array typed = column.Type switch
        {
            ReportColumnType.Date => values.Cast<DateOnly>().ToArray(),
            ReportColumnType.Text => values.Cast<string>().ToArray(),
            ReportColumnType.Integer => values.Cast<long>().ToArray(),
            ReportColumnType.Decimal => values.Cast<decimal>().ToArray(),
            _ => values.Cast<bool>().ToArray(),
        };
        return AddRaw(NpgsqlDbType.Array | ReportValueParser.DbTypeFor(column.Type), typed, parameters);
    }

    private static string AddRaw(NpgsqlDbType type, object value, List<NpgsqlParameter> parameters)
    {
        var name = $"p{parameters.Count}";
        parameters.Add(new NpgsqlParameter(name, type) { Value = value });
        return "@" + name;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
