using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudCliClient : IPlaudClient
{
    private readonly ILogger<PlaudCliClient> _logger;
    private readonly IOptions<PlaudOptions> _options;
    private readonly IPlaudTokenRefresher _tokenRefresher;

    /// <summary>
    /// Prefix Plaud's API puts on file ids (e.g. "of_61d13e01..."). Stored ids are kept bare so
    /// recordings ingested before the prefix appeared stay recognisable, but the CLI only resolves
    /// the prefixed form — a bare id returns NOT_FOUND.
    /// </summary>
    private const string CliIdPrefix = "of_";

    private static readonly Regex UpstreamStatusRegex =
        new(@"API error:\s*(5\d\d)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Detects a Plaud upstream 5xx ("API error: 500 Internal Server Error") in the CLI's stderr.
    /// </summary>
    public static bool TryParseUpstreamStatus(string? stderr, out int status)
    {
        status = 0;
        if (string.IsNullOrEmpty(stderr)) return false;
        var match = UpstreamStatusRegex.Match(stderr);
        return match.Success && int.TryParse(match.Groups[1].Value, out status);
    }

    public PlaudCliClient(ILogger<PlaudCliClient> logger, IOptions<PlaudOptions> options, IPlaudTokenRefresher tokenRefresher)
    {
        _logger = logger;
        _options = options;
        _tokenRefresher = tokenRefresher;
    }

    /// <summary>Converts a stored (bare) recording id into the prefixed form the CLI requires.</summary>
    public static string ToCliRecordingId(string recordingId) =>
        recordingId.StartsWith(CliIdPrefix, StringComparison.Ordinal) ? recordingId : CliIdPrefix + recordingId;

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
            await RunCliAsync(new[] { "transcript", ToCliRecordingId(recordingId), "-o", tempFile }, ct);
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
            await RunCliAsync(new[] { "summary", ToCliRecordingId(recordingId), "-o", tempFile }, ct);
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
            var output = await RunCliCoreAsync(args, ct);
            // The CLI rotates the on-disk refresh token during a normal (non-AUTH_FAILED) call.
            // Mirror it to Key Vault so a container restart never re-seeds a stale token. Best-effort.
            await _tokenRefresher.SyncToKeyVaultAsync(ct);
            return output;
        }
        catch (PlaudAuthExpiredException)
        {
            _logger.LogWarning("Plaud auth expired; attempting token refresh and retry.");
            try
            {
                await _tokenRefresher.RefreshAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plaud token refresh failed.");
                throw new PlaudAuthExpiredException("token refresh failed", ex);
            }

            _logger.LogInformation("Plaud token refreshed; retrying CLI call.");
            // A second AUTH_FAILED here means the refreshed token is still rejected — surface it.
            // RefreshAsync already persisted the fresh token to Key Vault, so no extra sync needed.
            return await RunCliCoreAsync(args, ct);
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

            // Declared after `using var process`, so it is disposed first and cannot fire on a disposed Process.
            using var killRegistration = cts.Token.Register(() => TryKillProcessTree(process, _logger));

            await process.WaitForExitAsync(cts.Token);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                var trimmed = (error ?? string.Empty).Trim();
                if (trimmed.Contains("AUTH_FAILED", StringComparison.Ordinal))
                {
                    _logger.LogError("Plaud CLI exited with code {ExitCode}: {Error}", process.ExitCode, error);
                    throw new PlaudAuthExpiredException(trimmed);
                }
                var suffix = trimmed.Length > 0 ? $": {trimmed}" : string.Empty;
                var message = $"Plaud CLI exited with code {process.ExitCode}{suffix}";
                if (TryParseUpstreamStatus(trimmed, out var upstreamStatus))
                {
                    _logger.LogWarning("Plaud CLI upstream error {Status}: {Error}", upstreamStatus, trimmed);
                    throw new PlaudUpstreamUnavailableException(upstreamStatus, message);
                }
                _logger.LogError("Plaud CLI exited with code {ExitCode}: {Error}", process.ExitCode, error);
                throw new InvalidOperationException(message);
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

    /// <summary>
    /// Kills the CLI process tree. Runs from a <see cref="CancellationToken"/> callback on a timer
    /// thread, where any escaping exception terminates the whole host — so it must never throw.
    /// </summary>
    internal static void TryKillProcessTree(Process process, ILogger logger)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogDebug(ex, "Plaud CLI process already exited; nothing to kill.");
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning(ex, "Failed to kill Plaud CLI process tree.");
        }
        catch (NotSupportedException ex)
        {
            logger.LogWarning(ex, "Killing Plaud CLI process tree is not supported.");
        }
        catch (Exception ex)
        {
            // Last-resort guard: this runs on a timer thread where any escape is fatal.
            logger.LogWarning(ex, "Unexpected error killing Plaud CLI process tree.");
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

            // Plaud returns ids prefixed with "of_"; store them bare to stay consistent with
            // recordings ingested before the prefix existed.
            var id = tokens[0].StartsWith(CliIdPrefix, StringComparison.Ordinal)
                ? tokens[0][CliIdPrefix.Length..]
                : tokens[0];

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
        var output = await RunCliAsync(new[] { "file", ToCliRecordingId(recordingId) }, ct);
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
