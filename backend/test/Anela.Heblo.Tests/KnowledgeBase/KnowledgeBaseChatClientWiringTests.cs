using Anela.Heblo.Adapters.Anthropic;
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase;

/// <summary>
/// Proves the fix for the Anthropic adapter baking KnowledgeBase's answer-enrichment
/// middleware into the default (unkeyed) IChatClient: the default client, shared by
/// non-KnowledgeBase consumers, must stay plain, while KnowledgeBase's own keyed client
/// must still carry the enrichment.
/// </summary>
public class KnowledgeBaseChatClientWiringTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAnthropicAdapter(configuration);
        services.AddKnowledgeBaseModule(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Default_chat_client_has_no_enrichment_middleware()
    {
        using var provider = BuildProvider();

        var chatClient = provider.GetRequiredService<IChatClient>();

        chatClient.Should().NotBeOfType<PostAnswerEnrichmentMiddleware>();
        chatClient.GetService(typeof(PostAnswerEnrichmentMiddleware)).Should().BeNull();
    }

    [Fact]
    public void KnowledgeBase_keyed_chat_client_wraps_enrichment_middleware()
    {
        using var provider = BuildProvider();

        var chatClient = provider.GetRequiredKeyedService<IChatClient>(KnowledgeBaseConstants.EnrichedChatClientKey);

        chatClient.Should().BeOfType<PostAnswerEnrichmentMiddleware>();
    }
}
