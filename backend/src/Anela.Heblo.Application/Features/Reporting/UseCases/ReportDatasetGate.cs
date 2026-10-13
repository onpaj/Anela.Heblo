using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using FuzzySharp;

namespace Anela.Heblo.Application.Features.Reporting.UseCases;

internal static class ReportDatasetGate
{
    private const int MaxSuggestions = 3;
    private const int MinSuggestionScore = 60;

    /// <summary>Returns the dataset, or the error code + params explaining why the caller cannot use it.</summary>
    public static (ReportDataset? Dataset, ErrorCodes? Error, Dictionary<string, string>? Params) Resolve(
        string id, ICurrentUserService user)
    {
        var dataset = ReportDatasetCatalog.Find(id);
        if (dataset is null)
        {
            var suggestions = Process.ExtractTop(id ?? string.Empty, ReportDatasetCatalog.All.Select(d => d.Id), limit: MaxSuggestions)
                .Where(r => r.Score >= MinSuggestionScore)
                .Select(r => r.Value);
            return (null, ErrorCodes.ReportingDatasetNotFound, new Dictionary<string, string>
            {
                ["Dataset"] = id ?? string.Empty,
                ["Suggestions"] = string.Join(", ", suggestions),
            });
        }

        if (!ReportAccess.CanRead(user, dataset.Source))
        {
            return (null, ErrorCodes.Forbidden, new Dictionary<string, string>
            {
                ["RequiredRole"] = ReportAccess.RequiredRole(dataset.Source),
            });
        }

        return (dataset, null, null);
    }
}
