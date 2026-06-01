using System.ComponentModel.DataAnnotations;
using MediatR;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Application.Features.Article.UseCases.GenerateArticle;

public class GenerateArticleRequest : IRequest<GenerateArticleResponse>
{
    [Required, MinLength(3), MaxLength(500)]
    public string Topic { get; set; } = string.Empty;

    public string Scope { get; set; } = DomainArticle.DefaultScope;
    public string? Audience { get; set; }
    public string? Angle { get; set; }
    public string Length { get; set; } = DomainArticle.DefaultLength;
    [MaxLength(500)]
    public string? LanguageNote { get; set; }
    public bool UseKnowledgeBase { get; set; } = true;
    public bool UseWebSearch { get; set; } = true;
    public string? StyleGuideDriveId { get; set; }
    public string? StyleGuideItemPath { get; set; }
}
