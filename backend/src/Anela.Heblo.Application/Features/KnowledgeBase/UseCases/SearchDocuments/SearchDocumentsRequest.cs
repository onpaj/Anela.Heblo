using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;

public class SearchDocumentsRequest : IRequest<SearchDocumentsResponse>
{
    [Required, MinLength(1), MaxLength(2000)]
    public string Query { get; set; } = string.Empty;

    [Range(1, 20)]
    public int TopK { get; set; } = 5;
}

public class SearchDocumentsResponse : BaseResponse
{
    public List<ChunkResult> Chunks { get; set; } = [];
}

public class ChunkResult
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
    public string SourceFilename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
}
