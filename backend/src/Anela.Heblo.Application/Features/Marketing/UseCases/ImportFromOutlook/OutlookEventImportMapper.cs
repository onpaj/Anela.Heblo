using System;
using System.Text.RegularExpressions;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook
{
    internal static class OutlookEventImportMapper
    {
        private const int MaxTitleLength = 200;
        private const int MaxDescriptionLength = 5000;

        private static readonly Regex ScriptStyleRegex = new(
            @"<(script|style)[^>]*>.*?</(script|style)>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex HtmlTagRegex = new(
            @"<[^>]+>",
            RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegex = new(
            @"\s+",
            RegexOptions.Compiled);

        internal static MarketingAction BuildAction(
            OutlookEventDto evt,
            CurrentUser currentUser,
            DateTime utcNow,
            MarketingActionType actionType)
        {
            var action = new MarketingAction
            {
                Title = ParseTitle(evt.Subject),
                Description = ParseDescription(evt.BodyText),
                ActionType = actionType,
                StartDate = evt.StartUtc,
                EndDate = ParseEndDate(evt),
                CreatedAt = utcNow,
                ModifiedAt = utcNow,
                CreatedByUserId = currentUser.Id!,
                CreatedByUsername = currentUser.Name ?? "Unknown User",
            };

            action.MarkOutlookSynced(evt.Id, utcNow);

            return action;
        }

        internal static bool HasChanges(MarketingAction existing, OutlookEventDto evt, MarketingActionType actionType)
        {
            return existing.Title != ParseTitle(evt.Subject)
                || existing.Description != ParseDescription(evt.BodyText)
                || existing.StartDate != evt.StartUtc
                || existing.EndDate != ParseEndDate(evt)
                || existing.ActionType != actionType;
        }

        internal static void ApplyChanges(
            MarketingAction existing,
            OutlookEventDto evt,
            MarketingActionType actionType,
            CurrentUser currentUser,
            DateTime utcNow)
        {
            existing.Title = ParseTitle(evt.Subject);
            existing.Description = ParseDescription(evt.BodyText);
            existing.StartDate = evt.StartUtc;
            existing.EndDate = ParseEndDate(evt);
            existing.ActionType = actionType;
            existing.ModifiedAt = utcNow;
            existing.ModifiedByUserId = currentUser.Id;
            existing.ModifiedByUsername = currentUser.Name ?? "Unknown User";
            existing.MarkOutlookSynced(evt.Id, utcNow);
        }

        private static string ParseTitle(string? subject)
        {
            var title = subject ?? string.Empty;
            return title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
        }

        private static string? ParseDescription(string? bodyText)
        {
            var stripped = StripHtml(bodyText);
            return stripped?.Length > MaxDescriptionLength ? stripped[..MaxDescriptionLength] : stripped;
        }

        private static DateTime? ParseEndDate(OutlookEventDto evt)
        {
            return evt.EndUtc == DateTime.MinValue || evt.EndUtc == evt.StartUtc
                ? null
                : evt.EndUtc;
        }

        private static string? StripHtml(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;

            var result = ScriptStyleRegex.Replace(html, string.Empty);
            result = HtmlTagRegex.Replace(result, string.Empty);
            result = System.Net.WebUtility.HtmlDecode(result);
            result = WhitespaceRegex.Replace(result, " ").Trim();

            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
    }
}
