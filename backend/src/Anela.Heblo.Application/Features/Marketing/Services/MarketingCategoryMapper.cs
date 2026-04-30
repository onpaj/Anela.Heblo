using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Domain.Features.Marketing;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    public sealed class MarketingCategoryMapper : IMarketingCategoryMapper, IDisposable
    {
        private readonly ILogger<MarketingCategoryMapper> _logger;
        private readonly IDisposable? _changeSubscription;
        private volatile Snapshot _snapshot;

        public MarketingCategoryMapper(
            IOptionsMonitor<MarketingCalendarOptions> optionsMonitor,
            ILogger<MarketingCategoryMapper> logger)
        {
            _logger = logger;
            _snapshot = BuildSnapshot(optionsMonitor.CurrentValue);
            _changeSubscription = optionsMonitor.OnChange(opts =>
            {
                try
                {
                    _snapshot = BuildSnapshot(opts);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to rebuild marketing category snapshot; keeping prior snapshot.");
                }
            });
        }

        public CategoryMappingResult MapToActionType(IReadOnlyList<string> outlookCategories)
        {
            var snap = _snapshot;

            if (outlookCategories is null || outlookCategories.Count == 0)
            {
                return new CategoryMappingResult(MarketingActionType.General, null, Array.Empty<string>());
            }

            List<string>? unmapped = null;

            foreach (var raw in outlookCategories)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                if (snap.Incoming.TryGetValue(raw, out var actionType))
                {
                    return new CategoryMappingResult(actionType, raw, Array.Empty<string>());
                }

                (unmapped ??= new List<string>()).Add(raw);
            }

            return new CategoryMappingResult(
                MarketingActionType.General,
                null,
                (IReadOnlyList<string>?)unmapped ?? Array.Empty<string>());
        }

        // Multiple action types may share the same Outlook name; round-trip is
        // not guaranteed to be injective. See Open Question 3 / FR-7.
        public string MapToOutlookCategory(MarketingActionType actionType)
        {
            var snap = _snapshot;

            return snap.Outgoing.TryGetValue(actionType, out var name)
                ? name
                : actionType.ToString();
        }

        public void Dispose() => _changeSubscription?.Dispose();

        private static Snapshot BuildSnapshot(MarketingCalendarOptions opts)
        {
            var incoming = new Dictionary<string, MarketingActionType>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in opts.CategoryMappings ?? new Dictionary<string, MarketingActionType>())
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                {
                    continue;
                }

                incoming[kv.Key.Trim()] = kv.Value;
            }

            var outgoing = new Dictionary<MarketingActionType, string>();

            foreach (var kv in opts.OutgoingCategories ?? new Dictionary<MarketingActionType, string>())
            {
                outgoing[kv.Key] = (kv.Value ?? string.Empty).Trim();
            }

            return new Snapshot(incoming, outgoing);
        }

        private sealed record Snapshot(
            IReadOnlyDictionary<string, MarketingActionType> Incoming,
            IReadOnlyDictionary<MarketingActionType, string> Outgoing);
    }
}
