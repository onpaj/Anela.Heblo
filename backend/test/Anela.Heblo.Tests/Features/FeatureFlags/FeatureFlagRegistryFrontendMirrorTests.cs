using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anela.Heblo.Application.Features.FeatureFlags;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.FeatureFlags;

/// <summary>
/// Guards against the frontend flag mirror and base appsettings.json drifting
/// from FeatureFlagRegistry (the source of truth) — see docs/development/feature-flags.md.
/// </summary>
public class FeatureFlagRegistryFrontendMirrorTests
{
    [Fact]
    public void FrontendMirror_AllKeys_ExistInBackendRegistry()
    {
        var frontendValues = ExtractFrontendFlagValues(LoadRepoFile("frontend", "src", "features", "feature-flags", "featureFlags.ts"));
        var backendKeys = FeatureFlagRegistry.All.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);

        var orphaned = frontendValues.Where(v => !backendKeys.Contains(v)).ToList();

        orphaned.Should().BeEmpty(
            "frontend/src/features/feature-flags/featureFlags.ts declares flag keys with no " +
            "FeatureFlagRegistry.cs entry — delete the orphaned mirror or add the missing " +
            "registry entry (see docs/development/feature-flags.md)");
    }

    [Fact]
    public void AppSettings_FeatureManagement_ExactlyMatchesBackendRegistry()
    {
        var appSettingsJson = LoadRepoFile("backend", "src", "Anela.Heblo.API", "appsettings.json");
        using var document = JsonDocument.Parse(appSettingsJson, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        var featureManagementKeys = document.RootElement
            .GetProperty("FeatureManagement")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var registryKeys = FeatureFlagRegistry.All.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);

        featureManagementKeys.Should().BeEquivalentTo(registryKeys,
            "backend/src/Anela.Heblo.API/appsettings.json's FeatureManagement section must contain " +
            "exactly the keys in FeatureFlagRegistry.All — no more (orphaned defaults), no fewer " +
            "(missing defaults) — see docs/development/feature-flags.md");
    }

    private static string LoadRepoFile(params string[] relativePathSegments)
    {
        var relativePath = Path.Combine(relativePathSegments);
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var testDirectory = Path.GetDirectoryName(assemblyLocation)!;

        var projectRoot = testDirectory;
        while (projectRoot != null && !File.Exists(Path.Combine(projectRoot, relativePath)))
        {
            var parent = Directory.GetParent(projectRoot);
            if (parent == null) break;
            projectRoot = parent.FullName;
        }

        Assert.True(projectRoot != null, $"Could not find project root directory (looking for {relativePath})");

        var filePath = Path.Combine(projectRoot, relativePath);
        Assert.True(File.Exists(filePath), $"File not found at: {filePath}");

        return File.ReadAllText(filePath);
    }

    private static IReadOnlyList<string> ExtractFrontendFlagValues(string tsSource)
    {
        var objectLiteral = Regex.Match(tsSource, @"FeatureFlagKeys\s*=\s*\{(.*?)\}\s*as const", RegexOptions.Singleline);
        Assert.True(objectLiteral.Success, "Could not locate FeatureFlagKeys object literal in featureFlags.ts");

        return Regex.Matches(objectLiteral.Groups[1].Value, @":\s*""([\w-]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }
}
