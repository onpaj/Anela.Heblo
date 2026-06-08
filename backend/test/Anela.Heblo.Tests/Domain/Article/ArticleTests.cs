using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Article
{
    public class ArticleTests
    {
        [Fact]
        public void SubmitFeedback_AssignsAllThreeProperties()
        {
            var article = new Heblo.Domain.Features.Article.Article
            {
                Id = Guid.NewGuid(),
                Topic = "topic",
            };

            article.PrecisionScore.Should().BeNull();
            article.StyleScore.Should().BeNull();
            article.FeedbackComment.Should().BeNull();

            article.SubmitFeedback(precisionScore: 5, styleScore: 4, comment: "Great");

            article.PrecisionScore.Should().Be(5);
            article.StyleScore.Should().Be(4);
            article.FeedbackComment.Should().Be("Great");
        }

        [Fact]
        public void SubmitFeedback_NullComment_IsAllowed()
        {
            var article = new Heblo.Domain.Features.Article.Article
            {
                Id = Guid.NewGuid(),
                Topic = "topic",
            };

            var act = () => article.SubmitFeedback(precisionScore: 3, styleScore: 3, comment: null);

            act.Should().NotThrow();
            article.PrecisionScore.Should().Be(3);
            article.StyleScore.Should().Be(3);
            article.FeedbackComment.Should().BeNull();
        }
    }
}
