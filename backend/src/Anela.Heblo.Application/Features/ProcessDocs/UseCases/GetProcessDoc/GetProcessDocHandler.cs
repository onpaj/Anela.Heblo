using Anela.Heblo.Application.Features.ProcessDocs.Contracts;
using FuzzySharp;
using MediatR;

namespace Anela.Heblo.Application.Features.ProcessDocs.UseCases.GetProcessDoc;

public class GetProcessDocHandler : IRequestHandler<GetProcessDocRequest, GetProcessDocResponse>
{
    private const int MaxSuggestions = 3;
    private const int MinSuggestionScore = 60;

    private readonly IProcessDocStore _store;

    public GetProcessDocHandler(IProcessDocStore store)
    {
        _store = store;
    }

    public Task<GetProcessDocResponse> Handle(GetProcessDocRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Task.FromResult(new GetProcessDocResponse());
        }

        var doc = _store.Find(request.Name);
        if (doc is not null)
        {
            return Task.FromResult(new GetProcessDocResponse { Doc = ToDto(doc) });
        }

        var suggestions = Process.ExtractTop(request.Name, _store.All.Select(d => d.Name), limit: MaxSuggestions)
            .Where(r => r.Score >= MinSuggestionScore)
            .Select(r => r.Value)
            .ToList();

        return Task.FromResult(new GetProcessDocResponse { Suggestions = suggestions });
    }

    private static ProcessDocDto ToDto(ProcessDoc doc) => new()
    {
        Name = doc.Name,
        Kind = doc.Kind,
        Summary = doc.Summary,
        VerifiedAt = doc.VerifiedAt,
        Related = doc.Related.ToList(),
        Markdown = doc.Markdown,
    };
}
