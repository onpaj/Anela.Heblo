using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase;

public class KnowledgeBaseOptionsTests
{
    [Fact]
    public void DefaultQueryExpansionPrompt_ContainsTagPlaceholderExactlyOnce()
    {
        var prompt = new KnowledgeBaseOptions().QueryExpansionPrompt;

        var occurrences = prompt.Split(QuestionTagLibrary.Placeholder).Length - 1;

        occurrences.Should().Be(1);
    }

    [Theory]
    [InlineData("\"expanded\"")]
    [InlineData("\"problemTags\"")]
    [InlineData("\"productTags\"")]
    public void DefaultQueryExpansionPrompt_ContainsJsonKey(string key)
    {
        new KnowledgeBaseOptions().QueryExpansionPrompt.Should().Contain(key);
    }

    [Fact]
    public void DefaultQueryExpansionPrompt_InstructsEscapedLineBreaksInExpanded()
    {
        new KnowledgeBaseOptions().QueryExpansionPrompt.Should().Contain("\\n");
    }
}
