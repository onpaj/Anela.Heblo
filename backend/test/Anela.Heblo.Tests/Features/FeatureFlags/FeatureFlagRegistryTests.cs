using System.Reflection;
using Anela.Heblo.Application.Features.FeatureFlags;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.FeatureFlags;

public class FeatureFlagRegistryTests
{
    [Fact]
    public void AllKeys_HaveRegistryEntry()
    {
        var constantKeys = typeof(FeatureFlagKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        var registryKeys = FeatureFlagRegistry.All.Select(d => d.Key).ToList();

        foreach (var key in constantKeys)
            registryKeys.Should().Contain(key, because: $"FeatureFlagKeys.{key} must have a registry entry");
    }

    [Fact]
    public void AllRegistryEntries_HaveNonEmptyDescription()
    {
        FeatureFlagRegistry.All.Should().AllSatisfy(def =>
            def.Description.Should().NotBeNullOrWhiteSpace(
                because: $"flag '{def.Key}' must have a description"));
    }

    [Fact]
    public void AllRegistryKeys_FollowNamingConvention()
    {
        FeatureFlagRegistry.All.Should().AllSatisfy(def =>
        {
            def.Key.Should().StartWith("is-", because: "all flag keys must start with 'is-'");
            def.Key.Should().EndWith("-enabled", because: "all flag keys must end with '-enabled'");
            def.Key.Should().Be(def.Key.ToLower(), because: "flag keys must be lowercase kebab-case");
        });
    }

    [Fact]
    public void AllRegistryKeys_AreUnique()
    {
        FeatureFlagRegistry.All.Select(d => d.Key).Should().OnlyHaveUniqueItems();
    }
}
