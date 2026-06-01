namespace Anela.Heblo.Application.Shared.Rag;

public interface IWordWindowChunker
{
    IReadOnlyList<string> Chunk(string text, int size, int overlap);
}
