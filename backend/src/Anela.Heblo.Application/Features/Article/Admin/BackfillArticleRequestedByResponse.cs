using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Article.Admin;

public sealed class BackfillArticleRequestedByResponse : BaseResponse
{
    public BackfillArticleRequestedByResponse() { }

    public BackfillArticleRequestedByResponse(
        ErrorCodes errorCode,
        Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }

    public int Total { get; set; }
    public int AlreadyMigrated { get; set; }
    public int Resolved { get; set; }
    public int Ambiguous { get; set; }
    public int Unresolved { get; set; }
    public bool WasDryRun { get; set; }
    public List<UnresolvedArticleRow> UnresolvedRows { get; set; } = new();
}
