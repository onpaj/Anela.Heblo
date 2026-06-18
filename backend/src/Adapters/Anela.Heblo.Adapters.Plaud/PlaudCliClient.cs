using System.Diagnostics;
using System.Text.Json;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudCliClient : IPlaudClient
{
    private readonly ILogger<PlaudCliClient> _logger;
    private readonly IOptions<PlaudOptions> _options;
    private readonly IPlaudTokenRefreshClient _refreshClient;
    private readonly string _tokensFilePath;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public PlaudCliClient(ILogger<PlaudCliClient> logger, IOptions<PlaudOptions> options, IPlaudTokenRefreshClient refreshClient)
        : this(logger, options, refreshClient,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".plaud", "tokens.json"))
    { }

    internal PlaudCliClient(ILogger<PlaudCliClient> logger, IOptions<PlaudOptions> options, IPlaudTokenRefreshClient refreshClient, string tokensFilePath)
    {
        _logger = logger;
        _options = options;
        _refreshClient = refreshClient;
        _tokensFilePath = tokensFilePath;
    }

    public async Task<List<PlaudRecordingSummary>> ListRecentAsync(int days, CancellationToken ct = default)
    {
        var args = new[] { "recent", "--days", days.ToString() };
        var output = await RunCliAsync(args, ct);
        return ParseFilesOutput(output);
    }

    public async Task<string> GetTranscriptAsync(string recordingId, CancellationToken ct = default)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await RunCliAsync(new[] { "transcript", recordingId, "-o", tempFile }, ct);
            return await File.ReadAllTextAsync(tempFile, ct);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    public async Task<PlaudSummaryResult> GetSummaryAsync(string recordingId, CancellationToken ct = default)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await RunCliAsync(new[] { "summary", recordingId, "-o", tempFile }, ct);
            var json = await File.ReadAllTextAsync(tempFile, ct);
            return ParseSummaryJson(json);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    public static PlaudSummaryResult ParseSummaryJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var headline = root.TryGetProperty("header", out var header) &&
                           header.TryGetProperty("headline", out var h)
                ? h.GetString() ?? string.Empty
                : string.Empty;

            var content = root.TryGetProperty("ai_content", out var c)
                ? c.GetString() ?? string.Empty
                : string.Empty;

            return new PlaudSummaryResult(headline, content);
        }
        catch (JsonException)
        {
            return new PlaudSummaryResult(string.Empty, json);
        }
    }

    private async Task<string> RunCliAsync(string[] args, CancellationToken ct)
    {
        try
        {
            return await RunCliCoreAsync(args, ct);
        }
        catch (PlaudAuthExpiredException)
        {
            _logger.LogWarning("Plaud auth expired; attempting token refresh and retry.");
            try
            {
                await RefreshTokensAsync(ct);
                _logger.LogInformation("Plaud token refreshed; retrying CLI call.");
                return await RunCliCoreAsync(args, ct);
            }
            catch (PlaudAuthExpiredException)
            {
                _logger.LogError("Plaud auth still expired after token refresh; giving up.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plaud token refresh failed.");
                throw new PlaudAuthExpiredException("token refresh failed", ex);
            }
        }
    }

    private async Task RefreshTokensAsync(CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_tokensFilePath))
                throw new InvalidOperationException(
                    $"Plaud tokens file not found at {_tokensFilePath}. Cannot refresh.");

            var diskJson = await File.ReadAllTextAsync(_tokensFilePath, ct);
            var diskTokens = JsonSerializer.Deserialize<PlaudTokens>(diskJson)
                ?? throw new InvalidOperationException("Failed to deserialize Plaud tokens from disk.");

            var newTokens = await _refreshClient.RefreshAsync(diskTokens.RefreshToken, ct);
            var newJson = JsonSerializer.Serialize(newTokens);

            await File.WriteAllTextAsync(_tokensFilePath, newJson, ct);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(_tokensFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<string> RunCliCoreAsync(string[] args, CancellationToken ct)
    {
        var options = _options.Value;
        var psi = new ProcessStartInfo
        {
            FileName = options.CliExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.ProcessTimeoutSeconds));

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            cts.Token.Register(() => process.Kill(entireProcessTree: true));

            await process.WaitForExitAsync(cts.Token);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogError("Plaud CLI exited with code {ExitCode}: {Error}", process.ExitCode, error);
                var trimmed = (error ?? string.Empty).Trim();
                if (trimmed.Contains("AUTH_FAILED", StringComparison.Ordinal))
                {
                    throw new PlaudAuthExpiredException(trimmed);
                }
                var suffix = trimmed.Length > 0 ? $": {trimmed}" : string.Empty;
                throw new InvalidOperationException(
                    $"Plaud CLI exited with code {process.ExitCode}{suffix}");
            }

            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("Plaud CLI stderr output: {Error}", error);
            }

            return output;
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Plaud CLI process exceeded {options.ProcessTimeoutSeconds} seconds timeout");
        }
    }

    public static List<PlaudRecordingSummary> ParseFilesOutput(string output)
    {
        var result = new List<PlaudRecordingSummary>();

        if (string.IsNullOrWhiteSpace(output))
            return result;

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // Real CLI format (one data row per recording, no transcript/summary columns):
        //   ID  [NAME...]  DATE  DURATION
        // First non-empty line is the summary header ("Recordings in the last N days: X") — skip it.
        for (int i = 1; i < lines.Length; i++)
        {
            var tokens = lines[i].Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            // Need at least: ID, DATE, DURATION
            if (tokens.Length < 3)
                continue;

            var id = tokens[0];

            // Validate ID is a 32-char lowercase hex string
            if (id.Length != 32 || !id.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                continue;

            // Second-to-last token is the recording date (yyyy-MM-dd); last token is duration (ignored)
            var dateStr = tokens[tokens.Length - 2];
            DateTime.TryParse(dateStr, out var createdAt);

            // Everything between ID and the last two tokens is the name
            var nameTokens = tokens.Skip(1).Take(tokens.Length - 3).ToArray();
            var name = string.Join(" ", nameTokens);

            result.Add(new PlaudRecordingSummary
            {
                Id = id,
                Name = name,
                CreatedAt = createdAt
            });
        }

        return result;
    }

    public async Task<PlaudFileDetail> GetFileDetailAsync(string recordingId, CancellationToken ct = default)
    {
        var output = await RunCliAsync(new[] { "file", recordingId }, ct);
        return ParseFileDetail(output);
    }

    public static PlaudFileDetail ParseFileDetail(string output)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0) continue;

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            if (!string.IsNullOrEmpty(key) && !key.StartsWith('-') && !key.StartsWith("File"))
                lookup[key] = value;
        }

        static bool IsAvailable(Dictionary<string, string> d, string k) =>
            d.TryGetValue(k, out var v) && v.Equals("available", StringComparison.OrdinalIgnoreCase);

        return new PlaudFileDetail
        {
            TranscriptAvailable = IsAvailable(lookup, "transcript"),
            SummaryAvailable = IsAvailable(lookup, "summary"),
            AudioAvailable = IsAvailable(lookup, "audio")
        };
    }
}
