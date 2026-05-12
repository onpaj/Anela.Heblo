using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Anela.Heblo.Domain.Features.Photobank;

public static class TagRuleMatcher
{
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();

    public static IReadOnlyList<string> GetMatchingTags(
        string folderPath,
        string fileName,
        IEnumerable<TagRule> rules)
    {
        if (string.IsNullOrEmpty(folderPath) && string.IsNullOrEmpty(fileName))
            return Array.Empty<string>();

        var virtualPath = string.IsNullOrEmpty(fileName)
            ? folderPath
            : folderPath + "/" + fileName;

        return rules
            .Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder)
            .Where(r => Matches(virtualPath, r.PathPattern))
            .Select(r => r.TagName.ToLowerInvariant())
            .Distinct()
            .ToList();
    }

    private static bool Matches(string virtualPath, string pattern)
    {
        var regex = RegexCache.GetOrAdd(
            pattern,
            p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));

        return regex.IsMatch(virtualPath);
    }
}
