using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Article.Admin;

public sealed class BackfillArticleRequestedByHandler
    : IRequestHandler<BackfillArticleRequestedByCommand, BackfillArticleRequestedByResponse>
{
    private readonly IArticleAdminRepository _repository;
    private readonly IArticleUserResolver _userResolver;
    private readonly ILogger<BackfillArticleRequestedByHandler> _logger;

    public BackfillArticleRequestedByHandler(
        IArticleAdminRepository repository,
        IArticleUserResolver userResolver,
        ILogger<BackfillArticleRequestedByHandler> logger)
    {
        _repository = repository;
        _userResolver = userResolver;
        _logger = logger;
    }

    public async Task<BackfillArticleRequestedByResponse> Handle(
        BackfillArticleRequestedByCommand request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.GroupId))
        {
            return new BackfillArticleRequestedByResponse(
                ErrorCodes.ValidationError,
                new Dictionary<string, string> { { "field", "GroupId" } });
        }

        var members = await _userResolver.ResolveByGroupAsync(request.GroupId, ct);
        var byDisplayName = members
            .GroupBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var rows = await _repository.ListWithRequestedByAsync(ct);
        var response = new BackfillArticleRequestedByResponse
        {
            Total = rows.Count,
            WasDryRun = request.DryRun,
        };

        var anyResolved = false;

        foreach (var row in rows)
        {
            var original = row.RequestedBy!;

            if (LooksLikeIdentifier(original))
            {
                response.AlreadyMigrated++;
                _logger.LogInformation(
                    "Article {ArticleId} RequestedBy={Value} already looks like an identifier; skipping.",
                    row.Id, original);
                continue;
            }

            if (!byDisplayName.TryGetValue(original, out var matches))
            {
                response.Unresolved++;
                response.UnresolvedRows.Add(new UnresolvedArticleRow
                {
                    ArticleId = row.Id,
                    OriginalValue = original,
                    Reason = "no match in Graph group members",
                });
                _logger.LogWarning(
                    "Article {ArticleId} RequestedBy={Value} has no match in group {GroupId}.",
                    row.Id, original, request.GroupId);
                continue;
            }

            if (matches.Count > 1)
            {
                response.Ambiguous++;
                response.UnresolvedRows.Add(new UnresolvedArticleRow
                {
                    ArticleId = row.Id,
                    OriginalValue = original,
                    Reason = $"ambiguous: {matches.Count} group members share this display name",
                });
                _logger.LogWarning(
                    "Article {ArticleId} RequestedBy={Value} is ambiguous ({Count} matches).",
                    row.Id, original, matches.Count);
                continue;
            }

            var match = matches[0];
            if (!request.DryRun)
            {
                row.RequestedBy = match.Id;
            }
            anyResolved = true;
            response.Resolved++;
            _logger.LogInformation(
                "Article {ArticleId} resolved: {DisplayName} -> {Id}.",
                row.Id, original, match.Id);
        }

        if (anyResolved && !request.DryRun)
        {
            await _repository.SaveChangesAsync(ct);
        }

        return response;
    }

    private static bool LooksLikeIdentifier(string value)
    {
        if (Guid.TryParse(value, out _))
        {
            return true;
        }

        return value.Contains('@', StringComparison.Ordinal);
    }
}
