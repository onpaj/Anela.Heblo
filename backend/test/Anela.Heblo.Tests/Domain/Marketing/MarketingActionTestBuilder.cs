using System;
using Anela.Heblo.Domain.Features.Marketing;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    internal sealed class MarketingActionTestBuilder
    {
        private int _id;
        private string _title = "Test Action";
        private string? _description;
        private MarketingActionType _actionType = MarketingActionType.SocialMedia;
        private DateTime _startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private DateTime? _endDate;
        private DateTime _createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private DateTime _modifiedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private string _createdByUserId = "user-1";
        private string? _createdByUsername;
        private string? _modifiedByUserId;
        private string? _modifiedByUsername;
        private string? _outlookEventId;
        private MarketingSyncStatus _outlookSyncStatus = MarketingSyncStatus.NotSynced;

        public MarketingActionTestBuilder WithId(int id) { _id = id; return this; }
        public MarketingActionTestBuilder WithTitle(string title) { _title = title; return this; }
        public MarketingActionTestBuilder WithDescription(string? description) { _description = description; return this; }
        public MarketingActionTestBuilder WithActionType(MarketingActionType type) { _actionType = type; return this; }
        public MarketingActionTestBuilder WithStartDate(DateTime startDate) { _startDate = startDate; return this; }
        public MarketingActionTestBuilder WithEndDate(DateTime? endDate) { _endDate = endDate; return this; }
        public MarketingActionTestBuilder WithCreatedAt(DateTime createdAt) { _createdAt = createdAt; return this; }
        public MarketingActionTestBuilder WithModifiedAt(DateTime modifiedAt) { _modifiedAt = modifiedAt; return this; }
        public MarketingActionTestBuilder WithCreatedBy(string userId, string? username = null)
        {
            _createdByUserId = userId; _createdByUsername = username; return this;
        }
        public MarketingActionTestBuilder WithModifiedBy(string? userId, string? username = null)
        {
            _modifiedByUserId = userId; _modifiedByUsername = username; return this;
        }
        public MarketingActionTestBuilder WithOutlookEventId(string? eventId)
        {
            _outlookEventId = eventId; return this;
        }
        public MarketingActionTestBuilder WithOutlookSyncStatus(MarketingSyncStatus status)
        {
            _outlookSyncStatus = status; return this;
        }

        public MarketingAction Build()
        {
            // Construct via the public ctor (FR-1). Then layer modified-audit
            // and Id/Outlook fields on top — the latter still expose public setters.
            var action = new MarketingAction(
                title: _title,
                description: _description,
                actionType: _actionType,
                startDate: _startDate,
                endDate: _endDate,
                createdByUserId: _createdByUserId,
                createdByUsername: _createdByUsername,
                utcNow: _createdAt);

            if (_modifiedByUserId is not null || _modifiedByUsername is not null || _modifiedAt != _createdAt)
            {
                action.UpdateDetails(
                    title: _title,
                    description: _description,
                    actionType: _actionType,
                    startDate: _startDate,
                    endDate: _endDate,
                    modifiedByUserId: _modifiedByUserId ?? _createdByUserId,
                    modifiedByUsername: _modifiedByUsername,
                    utcNow: _modifiedAt);
            }

            action.Id = _id;
            action.OutlookEventId = _outlookEventId;
            action.OutlookSyncStatus = _outlookSyncStatus;
            return action;
        }
    }
}
