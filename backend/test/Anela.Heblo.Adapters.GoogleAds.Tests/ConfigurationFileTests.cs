using System.Text.Json;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests;

/// <summary>
/// Guards the never-write-live rule: Staging and Development share the live Google Ads account,
/// so only appsettings.Production.json may turn validate-only off.
/// </summary>
public sealed class ConfigurationFileTests
{
    private const string ProductionFileName = "appsettings.Production.json";
    private const string BaseFileName = "appsettings.json";

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static string ApiProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Anela.Heblo.sln")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the repository root containing Anela.Heblo.sln must be above the test binaries");
        return Path.Combine(directory!.FullName, "backend", "src", "Anela.Heblo.API");
    }

    private static bool? ReadValidateOnly(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path), ParseOptions);
        if (!document.RootElement.TryGetProperty("GoogleAds", out var section)
            || !section.TryGetProperty("ValidateOnly", out var value))
            return null;

        return value.GetBoolean();
    }

    [Fact]
    public void only_production_settings_may_turn_validate_only_off()
    {
        var files = Directory.GetFiles(ApiProjectDirectory(), "appsettings*.json");
        files.Should().Contain(f => Path.GetFileName(f) == ProductionFileName);

        var turnedOff = files
            .Where(f => ReadValidateOnly(f) == false)
            .Select(Path.GetFileName)
            .ToList();

        turnedOff.Should().BeEquivalentTo(new[] { ProductionFileName });
    }

    [Fact]
    public void base_settings_enable_validate_only()
    {
        ReadValidateOnly(Path.Combine(ApiProjectDirectory(), BaseFileName)).Should().BeTrue();
    }
}
