using System.Collections.Concurrent;

namespace Anela.Heblo.Xcc.Audit;

public class InMemoryDataLoadAuditService : IDataLoadAuditService
{
    private readonly ConcurrentDictionary<string, DataLoadAuditEntry> _auditLogs = new();
    private readonly int _maxEntries = 10000; // Limit memory usage

    public Task LogDataLoadAsync(string dataType, string source, int recordCount, bool success, Dictionary<string, object>? parameters = null, string? errorMessage = null, TimeSpan? duration = null)
    {
        var entry = new DataLoadAuditEntry
        {
            DataType = dataType,
            Source = source,
            RecordCount = recordCount,
            Success = success,
            ErrorMessage = errorMessage,
            Parameters = parameters ?? new Dictionary<string, object>(),
            Duration = duration ?? TimeSpan.Zero
        };

        AddEntry(entry);
        return Task.CompletedTask;
    }

    public Task<DataLoadAuditEntry> StartDataLoadAuditAsync(string dataType, string source, Dictionary<string, object>? parameters = null)
    {
        var entry = new DataLoadAuditEntry
        {
            DataType = dataType,
            Source = source,
            Parameters = parameters ?? new Dictionary<string, object>(),
            Success = false // Will be updated on completion
        };

        AddEntry(entry);
        return Task.FromResult(entry);
    }

    public Task CompleteDataLoadAuditAsync(string auditId, int recordCount, bool success, string? errorMessage = null)
    {
        if (_auditLogs.TryGetValue(auditId, out var entry))
        {
            var completionTime = DateTime.UtcNow;
            entry.RecordCount = recordCount;
            entry.Success = success;
            entry.ErrorMessage = errorMessage;
            entry.Duration = completionTime - entry.Timestamp;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DataLoadAuditEntry>> GetAuditLogsAsync(int? limit = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _auditLogs.Values.AsEnumerable();

        if (fromDate.HasValue)
        {
            query = query.Where(x => x.Timestamp >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(x => x.Timestamp <= toDate.Value);
        }

        var results = query
            .OrderByDescending(x => x.Timestamp)
            .Take(limit ?? 1000)
            .ToList();

        return Task.FromResult<IReadOnlyList<DataLoadAuditEntry>>(results);
    }

    private void AddEntry(DataLoadAuditEntry entry)
    {
        _auditLogs[entry.Id] = entry;

        // Cleanup old entries if we exceed the limit
        if (_auditLogs.Count > _maxEntries)
        {
            var oldestEntries = _auditLogs.Values
                .OrderBy(x => x.Timestamp)
                .Take(_auditLogs.Count - _maxEntries + 100) // Remove extra to avoid frequent cleanups
                .ToList();

            foreach (var oldEntry in oldestEntries)
            {
                _auditLogs.TryRemove(oldEntry.Id, out _);
            }
        }
    }
}