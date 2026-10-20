using System.Text.Json;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

/// <summary>Schema-level validation of access-matrix.json (the hand-edited
/// source of truth). These tests run against the file in the repo root,
/// resolved relative to the test project.</summary>
public class AccessMatrixJsonTests
{
    private static AccessMatrixManifest LoadManifest()
    {
        // Repo root sits four levels above the test bin output:
        // bin/Debug/net8.0 → test/Anela.Heblo.Tests → test → backend → <repo root>
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..",
            "access-matrix.json"));

        path.Should().EndWith("access-matrix.json");
        File.Exists(path).Should().BeTrue($"expected access-matrix.json at {path}");

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<AccessMatrixManifest>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("Failed to deserialize access-matrix.json");
    }

    [Fact]
    public void Json_Deserializes_AndPopulatesAllSections()
    {
        var m = LoadManifest();

        m.BaseRole.Should().Be("heblo_user");
        m.Features.Should().NotBeEmpty();
        m.MenuPaths.Should().NotBeEmpty();
        m.SeedGroups.Should().NotBeEmpty();
    }

    [Fact]
    public void Json_HasNoDuplicateFeatureKeys()
    {
        var m = LoadManifest();
        var dupes = m.Features.GroupBy(f => f.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        dupes.Should().BeEmpty();
    }

    [Fact]
    public void Json_HasNoDuplicateSeedGroupNames()
    {
        var m = LoadManifest();
        var dupes = m.SeedGroups.GroupBy(g => g.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        dupes.Should().BeEmpty();
    }

    [Fact]
    public void MenuPath_Requirements_ReferenceKnownFeatures()
    {
        var m = LoadManifest();
        var knownFeatures = m.Features.Select(f => f.Key).ToHashSet();

        var unknown = m.MenuPaths
            .SelectMany(mp => mp.Requires.Select(r => (mp.Path, r.Feature)))
            .Where(x => !knownFeatures.Contains(x.Feature))
            .ToList();

        unknown.Should().BeEmpty($"menuPaths reference features not declared in features[]: {string.Join(", ", unknown)}");
    }

    [Fact]
    public void MenuPath_Requirements_HaveValidLevels()
    {
        var m = LoadManifest();
        var validLevels = new[] { "Read", "Write", "Admin" };

        var invalid = m.MenuPaths
            .SelectMany(mp => mp.Requires.Select(r => (mp.Path, r.Level)))
            .Where(x => !validLevels.Contains(x.Level))
            .ToList();

        invalid.Should().BeEmpty();
    }

    [Fact]
    public void SeedGroup_Roles_AreValidPermissionStrings()
    {
        var m = LoadManifest();
        var knownFeatures = m.Features.ToDictionary(f => f.Key);

        foreach (var group in m.SeedGroups)
        {
            foreach (var role in group.Roles)
            {
                var parts = role.Split('.');
                parts.Length.Should().Be(3, $"role '{role}' in group '{group.Name}' is not 'module.feature.level'");

                var level = parts[2];
                level.Should().BeOneOf("read", "write", "admin");

                // Reconstruct PascalCase feature key from snake_case module + feature segments.
                var pascalModule = ToPascal(parts[0]);
                var pascalFeature = ToPascal(parts[1]);
                var featureKey = $"{pascalModule}_{pascalFeature}";

                knownFeatures.Should().ContainKey(featureKey,
                    $"role '{role}' in group '{group.Name}' references unknown feature '{featureKey}'");

                var f = knownFeatures[featureKey];
                if (level == "write") f.HasWrite.Should().BeTrue(
                    $"role '{role}' uses write level but feature '{featureKey}' is read-only");
                if (level == "admin") f.HasAdmin.Should().BeTrue(
                    $"role '{role}' uses admin level but feature '{featureKey}' lacks hasAdmin");
            }
        }

        static string ToPascal(string snake)
        {
            var sb = new System.Text.StringBuilder();
            bool upper = true;
            foreach (var c in snake)
            {
                if (c == '_') { upper = true; continue; }
                sb.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            return sb.ToString();
        }
    }

    private const string AdsRead = "marketing.ads.read";
    private const string AdsWrite = "marketing.ads.write";
    private const string ApprovalsRead = "marketing.ad_approvals.read";
    private const string ApprovalsWrite = "marketing.ad_approvals.write";
    private const string ApprovalsAdmin = "marketing.ad_approvals.admin";
    private const string ApproveViaMcp = "marketing.ad_approvals_via_mcp.read";

    public static TheoryData<string, string[]> MarketingAdsSeedGroupRoles => new()
    {
        { "Marketing_Agent", new[] { AdsRead, AdsWrite } },
        { "Marketer", new[] { AdsRead, AdsWrite } },
        { "Marketing_Specialist", new[] { AdsRead, AdsWrite, ApprovalsWrite } },
        { "Marketing_Chief", new[] { AdsRead, AdsWrite, ApprovalsWrite, ApproveViaMcp } },
        { "Spravce", new[] { AdsRead, AdsWrite, ApprovalsWrite, ApprovalsAdmin, ApproveViaMcp } },
    };

    [Theory]
    [MemberData(nameof(MarketingAdsSeedGroupRoles))]
    public void MarketingAds_SeedGroups_HoldExactlyTheAgreedAdsRoles(string groupName, string[] expected)
    {
        var m = LoadManifest();

        var group = m.SeedGroups.SingleOrDefault(g => g.Name == groupName);

        group.Should().NotBeNull($"seed group '{groupName}' must exist");
        group!.Roles.Where(r => r.StartsWith("marketing.ad", StringComparison.Ordinal))
            .Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void MarketingAgent_SeedGroup_HoldsNothingButAdsReadAndWrite()
    {
        var m = LoadManifest();

        var agent = m.SeedGroups.Single(g => g.Name == "Marketing_Agent");

        agent.Roles.Should().BeEquivalentTo(new[] { AdsRead, AdsWrite });
    }

    [Fact]
    public void OnlySpravce_HoldsAdApprovalsAdmin()
    {
        var m = LoadManifest();

        var holders = m.SeedGroups.Where(g => g.Roles.Contains(ApprovalsAdmin)).Select(g => g.Name);

        holders.Should().BeEquivalentTo(new[] { "Spravce" });
    }

    [Fact]
    public void NoSeedGroup_HoldsAdApprovalsRead()
    {
        var m = LoadManifest();

        var holders = m.SeedGroups.Where(g => g.Roles.Contains(ApprovalsRead)).Select(g => g.Name);

        holders.Should().BeEmpty("AdApprovals has no Read level in spec 7.2 and C3 says never grant it");
    }

    [Fact]
    public void EveryGroupThatMayApproveViaMcp_AlsoHoldsWebApprovalWrite()
    {
        var m = LoadManifest();

        var violators = m.SeedGroups
            .Where(g => g.Roles.Contains(ApproveViaMcp) && !g.Roles.Contains(ApprovalsWrite))
            .Select(g => g.Name)
            .ToList();

        violators.Should().BeEmpty("approve-via-MCP requires Marketing_AdApprovals Write as well (spec 7.2)");
    }
}
