namespace Anela.Heblo.Application.Shared.Rag;

public class WordWindowChunker : IWordWindowChunker
{
    public IReadOnlyList<string> Chunk(string text, int size, int overlap)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var step = Math.Max(1, size - overlap);
        var chunks = new List<string>();

        for (var start = 0; start < words.Length; start += step)
        {
            var take = Math.Min(size, words.Length - start);
            chunks.Add(string.Join(' ', words, start, take));

            if (start + size >= words.Length)
                break;
        }

        return chunks;
    }
}
