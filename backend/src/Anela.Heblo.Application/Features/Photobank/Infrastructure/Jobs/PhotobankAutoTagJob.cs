using System;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Shared.Json;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Photobank;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs;

public class PhotobankAutoTagJob : IRecurringJob
{
    private readonly IPhotobankRepository _repo;
    private readonly IChatClient _chat;
    private readonly AutoTagOptions _options;
    private readonly ILogger<PhotobankAutoTagJob> _logger;
    private readonly IPhotobankTagsCache _cache;
    private readonly IRecurringJobStatusChecker _statusChecker;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "photobank-auto-tag",
        DisplayName = "Photobank Auto-Tag",
        Description = "Sends untagged photos to the LLM and stamps validated tags back",
        CronExpression = "0 4 * * *",
        DefaultIsEnabled = false,
    };

    public PhotobankAutoTagJob(
        IPhotobankRepository repo,
        IChatClient chat,
        IOptions<AutoTagOptions> options,
        ILogger<PhotobankAutoTagJob> logger,
        IPhotobankTagsCache cache,
        IRecurringJobStatusChecker statusChecker)
    {
        _repo = repo;
        _chat = chat;
        _options = options.Value;
        _logger = logger;
        _cache = cache;
        _statusChecker = statusChecker;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // defaultIfMissing: false — LLM-cost job must stay off until an operator explicitly enables it in the DB.
        if (!await _statusChecker.IsJobEnabledAsync(
                Metadata.JobName,
                cancellationToken,
                defaultIfMissing: Metadata.DefaultIsEnabled))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var tagsByName = (await _repo.GetTagsWithCountsAsync(cancellationToken))
            .ToDictionary(t => t.Name, t => t.Id, StringComparer.Ordinal);

        _logger.LogInformation("{JobName} starting — vocabulary size: {VocabSize}", Metadata.JobName, tagsByName.Count);

        var processedCount = 0;
        var offset = 0;

        while (processedCount < _options.MaxPhotosPerRun)
        {
            var remaining = _options.MaxPhotosPerRun - processedCount;
            var pageSize = Math.Min(_options.BatchSize, remaining);

            var batch = await _repo.GetPhotosPendingAutoTagAsync(pageSize, offset, cancellationToken);
            if (batch.Count == 0) break;

            await ProcessBatchAsync(batch, tagsByName, cancellationToken);

            processedCount += batch.Count;
            offset += batch.Count;
        }

        _logger.LogInformation("{JobName} completed — {ProcessedCount} photos processed", Metadata.JobName, processedCount);
    }

    public async Task ExecuteForPhotosAsync(IReadOnlyList<PhotoAutoTagCandidate> candidates, CancellationToken ct)
    {
        var tagsByName = (await _repo.GetTagsWithCountsAsync(ct))
            .ToDictionary(t => t.Name, t => t.Id, StringComparer.Ordinal);

        for (var offset = 0; offset < candidates.Count; offset += _options.BatchSize)
        {
            var batch = candidates.Skip(offset).Take(_options.BatchSize).ToList();
            await ProcessBatchAsync(batch, tagsByName, ct);
        }
    }

    private async Task ProcessBatchAsync(
        IReadOnlyList<PhotoAutoTagCandidate> batch,
        Dictionary<string, int> tagsByName,
        CancellationToken ct)
    {
        var batchIds = batch.Select(p => p.Id).ToList();

        var systemPrompt = BuildSystemPrompt(tagsByName.Keys);
        var userPrompt = BuildUserPrompt(batch);

        ChatResponse response;
        try
        {
            response = await _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, userPrompt),
                ],
                new ChatOptions { ModelId = _options.Model },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed for batch of {Count} photos; skipping batch", batch.Count);
            return;
        }

        var raw = response.Text ?? string.Empty;
        var fallback = new AutoTagLlmPayload { Results = [] };
        var parsed = JsonResponseParser.ParseOrFallback(raw, fallback, _logger);

        foreach (var result in parsed.Results ?? [])
        {
            await ApplyTagsForPhotoAsync(result, tagsByName, ct);
        }

        await _repo.SaveChangesAsync(ct);
        await _repo.StampAutoTaggedAtAsync(batchIds, DateTime.UtcNow, ct);
        _cache.Invalidate();
    }

    private async Task ApplyTagsForPhotoAsync(
        AutoTagResult result,
        Dictionary<string, int> tagsByName,
        CancellationToken ct)
    {
        var validTags = (result.Tags ?? [])
            .Where(name => tagsByName.ContainsKey(name))
            .Distinct()
            .Take(_options.MaxTagsPerPhoto)
            .ToList();

        foreach (var tagName in validTags)
        {
            var tagId = tagsByName[tagName];

            if (await _repo.PhotoTagExistsAsync(result.Id, tagId, ct)) continue;

            await _repo.AddPhotoTagAsync(
                new PhotoTag
                {
                    PhotoId = result.Id,
                    TagId = tagId,
                    Source = PhotoTagSource.AI,
                    CreatedAt = DateTime.UtcNow,
                },
                ct);
        }
    }

    private static string BuildSystemPrompt(IEnumerable<string> vocabulary)
    {
        var vocabList = string.Join(", ", vocabulary);
        return $"""
            You are a photo tagging assistant for a cosmetics company.
            Tag each photo using ONLY tags from the following vocabulary (diacritic-sensitive, exact match required):
            {vocabList}

            Return JSON in this exact format, no markdown fences:
            """ + """{"results":[{"id":<photo_id>,"tags":["tag1","tag2"]}]}""";
    }

    private static string BuildUserPrompt(IReadOnlyList<PhotoAutoTagCandidate> batch)
    {
        var entries = batch.Select(p => $"id={p.Id} path={p.FolderPath}/{p.FileName}");
        return string.Join("\n", entries);
    }
}

internal sealed class AutoTagLlmPayload
{
    [JsonPropertyName("results")]
    public List<AutoTagResult>? Results { get; set; }
}

internal sealed class AutoTagResult
{
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Id { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}
