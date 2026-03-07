using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class DocumentChunker
{
    private readonly KnowledgeBaseOptions _options;

    public DocumentChunker(IOptions<KnowledgeBaseOptions> options)
    {
        _options = options.Value;
    }

    public List<string> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();
        var step = _options.ChunkSize - _options.ChunkOverlapTokens;

        for (int i = 0; i < words.Length; i += step)
        {
            var chunkWords = words.Skip(i).Take(_options.ChunkSize);
            chunks.Add(string.Join(" ", chunkWords));

            if (i + _options.ChunkSize >= words.Length)
            {
                break;
            }
        }

        return chunks;
    }
}
