using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Domain;

public class AdKillSwitchPolicyTests
{
    private static AdKillSwitch S(string scope, bool enabled) => new() { Scope = scope, ExecutionEnabled = enabled, UpdatedBy = "t" };

    [Fact]
    public void NoRows_FailsClosed() =>
        AdKillSwitchPolicy.IsExecutionEnabled(Array.Empty<AdKillSwitch>(), "GoogleAds").Should().BeFalse();

    [Fact]
    public void GlobalOff_DisablesEveryPlatform() =>
        AdKillSwitchPolicy.IsExecutionEnabled(new[] { S("Global", false), S("GoogleAds", true) }, "GoogleAds").Should().BeFalse();

    [Fact]
    public void GlobalOn_NoOverride_Enables() =>
        AdKillSwitchPolicy.IsExecutionEnabled(new[] { S("Global", true) }, "Sklik").Should().BeTrue();

    [Fact]
    public void GlobalOn_PlatformOverrideOff_DisablesThatPlatformOnly()
    {
        var switches = new[] { S("Global", true), S("MetaAds", false) };

        AdKillSwitchPolicy.IsExecutionEnabled(switches, "MetaAds").Should().BeFalse();
        AdKillSwitchPolicy.IsExecutionEnabled(switches, "GoogleAds").Should().BeTrue();
    }
}
