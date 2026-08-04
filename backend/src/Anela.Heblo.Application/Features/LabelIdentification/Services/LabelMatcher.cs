using FuzzySharp;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

public interface ILabelMatcher
{
    LabelMatchResult Match(string normalizedText);
}

/// <summary>
/// Ranks label reference families against normalized OCR text.
///
/// Matching is done at FAMILY level, not product-code level. Eleven of the 37 reference
/// labels are byte-identical to their size sibling because the 015/030 suffix is sticker
/// size, not composition — so a code-level index always ties and can never auto-confirm.
/// Grouping by family removes every tie; the operator picks the size afterwards.
/// </summary>
public sealed class LabelMatcher : ILabelMatcher
{
    private const double TokenSetWeight = 0.7;
    private const double JaccardWeight = 0.3;
    private const int MaxCandidates = 3;

    private readonly ILabelReferenceIndex _index;
    private readonly LabelIdentificationOptions _options;

    public LabelMatcher(ILabelReferenceIndex index, IOptions<LabelIdentificationOptions> options)
    {
        _index = index;
        _options = options.Value;
    }

    public LabelMatchResult Match(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return new LabelMatchResult
            {
                Decision = LabelMatchDecision.Low,
                Candidates = Array.Empty<LabelMatch>(),
            };
        }

        var queryTokens = LabelReferenceEntry.Tokenize(normalizedText);

        var ranked = _index.Entries
            .Select(entry => new LabelMatch
            {
                Family = entry.Family,
                Codes = entry.Codes,
                Score = Score(normalizedText, queryTokens, entry),
            })
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Family, StringComparer.Ordinal)
            .Take(MaxCandidates)
            .ToList();

        return new LabelMatchResult
        {
            Decision = Decide(ranked),
            Candidates = ranked,
        };
    }

    private static double Score(string query, IReadOnlySet<string> queryTokens, LabelReferenceEntry entry)
    {
        // token_set_ratio is robust against duplicated ghost text and reordering —
        // precisely the failure mode of photographing a roll of stickers.
        double tokenSet = Fuzz.TokenSetRatio(query, entry.Normalized);

        // Jaccard over comma-split ingredients sees boundaries word-level matching ignores.
        var union = queryTokens.Count + entry.Tokens.Count - queryTokens.Count(entry.Tokens.Contains);
        var intersection = queryTokens.Count(entry.Tokens.Contains);
        var jaccard = union == 0 ? 0d : (double)intersection / union * 100d;

        return TokenSetWeight * tokenSet + JaccardWeight * jaccard;
    }

    private LabelMatchDecision Decide(IReadOnlyList<LabelMatch> ranked)
    {
        if (ranked.Count == 0)
        {
            return LabelMatchDecision.Low;
        }

        var best = ranked[0].Score;
        var runnerUp = ranked.Count > 1 ? ranked[1].Score : 0d;

        if (best >= _options.AutoConfirmScore && best - runnerUp >= _options.AutoConfirmMargin)
        {
            return LabelMatchDecision.Auto;
        }

        return best >= _options.LowConfidenceFloor
            ? LabelMatchDecision.Choose
            : LabelMatchDecision.Low;
    }
}
