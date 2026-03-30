using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class ChatTranscriptPreprocessor
{
    private static readonly Regex ExcessiveBlankLines =
        new(@"\n{3,}", RegexOptions.Compiled);

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

        text = ExcessiveBlankLines.Replace(text, "\n\n");

        return text.Trim();
    }
}
