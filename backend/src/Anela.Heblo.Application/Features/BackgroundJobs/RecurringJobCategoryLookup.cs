using Anela.Heblo.Domain.Features.BackgroundJobs;

namespace Anela.Heblo.Application.Features.BackgroundJobs;

/// <summary>
/// Resolves a job's category from the discovered <see cref="IRecurringJob"/> implementations.
/// The category is developer-owned metadata that lives only in code, so it is joined onto
/// persisted configurations at read time rather than stored in the database.
/// </summary>
public static class RecurringJobCategoryLookup
{
    public static IReadOnlyDictionary<string, RecurringJobCategory> Build(IEnumerable<IRecurringJob> discoveredJobs)
    {
        ArgumentNullException.ThrowIfNull(discoveredJobs);

        return discoveredJobs
            .GroupBy(job => job.Metadata.JobName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Metadata.Category, StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns the category for <paramref name="jobName"/>, or
    /// <see cref="RecurringJobCategory.Uncategorized"/> when the stored configuration has
    /// no matching implementation in code (e.g. a job that was removed).
    /// </summary>
    public static RecurringJobCategory Resolve(
        IReadOnlyDictionary<string, RecurringJobCategory> categoriesByJobName,
        string jobName)
    {
        return categoriesByJobName.TryGetValue(jobName, out var category)
            ? category
            : RecurringJobCategory.Uncategorized;
    }
}
