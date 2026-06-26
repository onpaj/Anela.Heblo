using MediatR;

namespace Anela.Heblo.Application.Features.Article.Admin;

public sealed class BackfillArticleRequestedByCommand
    : IRequest<BackfillArticleRequestedByResponse>
{
    /// <summary>
    /// Entra group ID whose members are candidates for display-name → OID resolution.
    /// Required.
    /// </summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// When true (default), the command runs the resolution pass but does NOT
    /// persist any change to the database. Use for previewing.
    /// </summary>
    public bool DryRun { get; set; } = true;
}
