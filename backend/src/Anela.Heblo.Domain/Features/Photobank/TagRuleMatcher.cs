using System;
using System.Collections.Generic;
using System.Linq;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public static class TagRuleMatcher
    {
        /// <summary>
        /// Returns all tag names whose PathPattern matches the given folderPath.
        /// Only active rules are considered.
        /// Pattern matching: case-insensitive prefix, '*' matches exactly one segment.
        /// All matching rules apply (not first-match-wins).
        /// </summary>
        public static IReadOnlyList<string> GetMatchingTags(
            string folderPath,
            IEnumerable<TagRule> rules)
        {
            if (string.IsNullOrEmpty(folderPath)) return Array.Empty<string>();

            var pathSegments = folderPath
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            return rules
                .Where(r => r.IsActive)
                .OrderBy(r => r.SortOrder)
                .Where(r => Matches(pathSegments, r.PathPattern))
                .Select(r => r.TagName.ToLowerInvariant())
                .Distinct()
                .ToList();
        }

        private static bool Matches(string[] pathSegments, string pattern)
        {
            var patternSegments = pattern
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (patternSegments.Length > pathSegments.Length)
                return false;

            for (var i = 0; i < patternSegments.Length; i++)
            {
                var p = patternSegments[i];
                if (p == "*")
                {
                    // '*' matches exactly one segment — already consuming one via loop
                    continue;
                }

                if (!string.Equals(p, pathSegments[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }
    }
}
