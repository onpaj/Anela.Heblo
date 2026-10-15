using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Approval;

public class ApprovalChannelRegistrationTests
{
    private sealed class PlaceholderResolver : IApprovalChannelResolver
    {
        public ApprovalChannelResolution Resolve() => throw new NotSupportedException();
    }

    [Fact]
    public void AddApprovalChannel_ReplacesExistingResolver_AndBindsOptionsWithApiClientId()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:ClientId"] = "api-client",
            ["Ads:Approval:WebClientIds:0"] = "web-client",
            ["Ads:Approval:McpClientIds:0"] = "heblo-mcp-client",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<ICurrentUserService>());
        services.AddScoped<IApprovalChannelResolver, PlaceholderResolver>(); // stands in for C3's deny-all default

        // Act
        services.AddApprovalChannel(configuration);

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IApprovalChannelResolver>()
            .Should().ContainSingle().Which.Should().BeOfType<ApprovalChannelResolver>();
        var options = provider.GetRequiredService<IOptions<ApprovalChannelOptions>>().Value;
        options.ApiClientId.Should().Be("api-client");
        options.WebClientIds.Should().Equal("web-client");
        options.McpClientIds.Should().Equal("heblo-mcp-client");
    }
}
