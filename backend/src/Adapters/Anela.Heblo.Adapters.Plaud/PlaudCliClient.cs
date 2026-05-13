using System.Diagnostics;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudCliClient : IPlaudClient
{
    private readonly ILogger<PlaudCliClient> _logger;
    private readonly IOptions<PlaudOptions> _options;

    public PlaudCliClient(ILogger<PlaudCliClient> logger, IOptions<PlaudOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public async Task<List<PlaudRecordingSummary>> ListRecentAsync(int days, CancellationToken ct = default)
    {
        var args = new[] { "recent", "--days", days.ToString() };
        var output = await RunCliAsync(args, ct);
        return ParseFilesOutput(output);
    }

    public async Task<string> GetTranscriptAsync(string recordingId, CancellationToken ct = default)
    {
        var args = new[] { "transcript", recordingId };
        return await RunCliAsync(args, ct);
    }

    public async Task<string> GetSummaryAsync(string recordingId, CancellationToken ct = default)
    {
        var args = new[] { "summary", recordingId };
        return await RunCliAsync(args, ct);
    }

    private async Task<string> RunCliAsync(string[] args, CancellationToken ct)
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
                throw new InvalidOperationException($"Plaud CLI exited with code {process.ExitCode}");
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
        {
            return result;
        }

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
        {
            return result;
        }

        // Skip header line (first line)
        for (int i = 1; i < lines.Length; i++)
        {
            var tokens = lines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            // Skip rows with fewer than 6 tokens
            if (tokens.Length < 6)
            {
                continue;
            }

            // Last 4 tokens = date, time, hasTranscript, hasSummary
            var dateStr = tokens[tokens.Length - 4];
            var timeStr = tokens[tokens.Length - 3];
            var hasTranscriptStr = tokens[tokens.Length - 2];
            var hasSummaryStr = tokens[tokens.Length - 1];

            // First token is the ID
            var id = tokens[0];

            // Everything between first and last 4 tokens is the name
            var nameTokens = new string[tokens.Length - 5];
            Array.Copy(tokens, 1, nameTokens, 0, tokens.Length - 5);
            var name = string.Join(" ", nameTokens);

            // Parse CreatedAt from date and time
            var dateTimeStr = $"{dateStr} {timeStr}";
            var hasCreatedAt = DateTime.TryParse(dateTimeStr, out var createdAt);

            // Parse boolean flags (case-insensitive)
            var hasTranscript = hasTranscriptStr.Equals("yes", StringComparison.OrdinalIgnoreCase);
            var hasSummary = hasSummaryStr.Equals("yes", StringComparison.OrdinalIgnoreCase);

            result.Add(new PlaudRecordingSummary
            {
                Id = id,
                Name = name,
                CreatedAt = hasCreatedAt ? createdAt : default(DateTime),
                HasTranscript = hasTranscript,
                HasSummary = hasSummary
            });
        }

        return result;
    }
}
