using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class ChatTranscriptPreprocessor
{
    private readonly IReadOnlyList<Regex> _patterns;

    public ChatTranscriptPreprocessor(IOptions<KnowledgeBaseOptions> options)
    {
        _patterns = options.Value.PreprocessorPatterns
            .Select(p => new Regex(
                p,
                RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.Compiled))
            .ToList();
    }

    public string Clean(string rawText)
    {
        var text = rawText;

        foreach (var pattern in _patterns)
        {
            text = pattern.Replace(text, string.Empty);
        }

        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }
}
