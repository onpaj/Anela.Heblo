using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.Article.UseCases.GetArticle;
using Anela.Heblo.Application.Features.Article.UseCases.ListArticles;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;

namespace Anela.Heblo.Tests.Article.UseCases;

public class ArticleStatusWireFormatTests
{
    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    [Theory]
    [InlineData(ArticleStatus.Generated, "\"status\":\"Generated\"")]
    [InlineData(ArticleStatus.Queued, "\"status\":\"Queued\"")]
    public void GetArticleResponse_SerializesStatusAsEnumName(ArticleStatus status, string expectedFragment)
    {
        var response = new GetArticleResponse { Status = status };

        var json = JsonSerializer.Serialize(response, ApiJsonOptions);

        json.Should().Contain(expectedFragment);
    }

    [Theory]
    [InlineData(ArticleStatus.Generated, "\"status\":\"Generated\"")]
    [InlineData(ArticleStatus.Queued, "\"status\":\"Queued\"")]
    public void ArticleListItemDto_SerializesStatusAsEnumName(ArticleStatus status, string expectedFragment)
    {
        var item = new ArticleListItemDto { Status = status };

        var json = JsonSerializer.Serialize(item, ApiJsonOptions);

        json.Should().Contain(expectedFragment);
    }
}
