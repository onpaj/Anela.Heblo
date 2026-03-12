namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IAnswerService
{
    Task<string> GenerateAnswerAsync(
        string question,
        IEnumerable<string> contextChunks,
        CancellationToken ct = default);
}
