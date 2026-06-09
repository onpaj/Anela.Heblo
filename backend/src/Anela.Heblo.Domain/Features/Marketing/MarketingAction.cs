using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Marketing
{
    public class MarketingAction : IEntity<int>
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; private set; } = null!;

        [MaxLength(5000)]
        public string? Description { get; private set; }

        public MarketingActionType ActionType { get; private set; }

        [Required]
        public DateTime StartDate { get; private set; }

        public DateTime? EndDate { get; private set; }

        [Required]
        public DateTime CreatedAt { get; private set; }

        [Required]
        public DateTime ModifiedAt { get; private set; }

        [Required]
        [MaxLength(100)]
        public string CreatedByUserId { get; private set; } = null!;

        [MaxLength(100)]
        public string? CreatedByUsername { get; private set; }

        [MaxLength(100)]
        public string? ModifiedByUserId { get; private set; }

        [MaxLength(100)]
        public string? ModifiedByUsername { get; private set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

        [MaxLength(100)]
        public string? DeletedByUserId { get; set; }

        [MaxLength(100)]
        public string? DeletedByUsername { get; set; }

        // Outlook sync fields
        [MaxLength(500)]
        public string? OutlookEventId { get; set; }

        public DateTime? OutlookLastAttemptAt { get; set; }

        public MarketingSyncStatus OutlookSyncStatus { get; set; } = MarketingSyncStatus.NotSynced;

        [MaxLength(1000)]
        public string? OutlookSyncError { get; set; }

        // Navigation properties
        public virtual ICollection<MarketingActionProduct> ProductAssociations { get; set; } = new List<MarketingActionProduct>();
        public virtual ICollection<MarketingActionFolderLink> FolderLinks { get; set; } = new List<MarketingActionFolderLink>();

        public MarketingAction(
            string title,
            string? description,
            MarketingActionType actionType,
            DateTime startDate,
            DateTime? endDate,
            string createdByUserId,
            string? createdByUsername,
            DateTime utcNow)
        {
            Title = NormalizeTitle(title);
            Description = NormalizeDescription(description);
            ActionType = actionType;
            StartDate = startDate;
            EndDate = endDate;
            CreatedAt = utcNow;
            ModifiedAt = utcNow;
            CreatedByUserId = createdByUserId;
            CreatedByUsername = createdByUsername ?? "Unknown User";
        }

        private MarketingAction() { }

        // Domain methods
        public void AssociateWithProduct(string productCode, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                throw new ArgumentException("Product code cannot be empty", nameof(productCode));

            var normalized = productCode.Trim().ToUpperInvariant();

            if (ProductAssociations.Any(pa => pa.ProductCodePrefix == normalized))
                return;

            ProductAssociations.Add(new MarketingActionProduct
            {
                MarketingActionId = Id,
                ProductCodePrefix = normalized,
                CreatedAt = utcNow,
            });
        }

        public void LinkToFolder(string folderKey, MarketingFolderType folderType, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(folderKey))
                throw new ArgumentException("Folder key cannot be empty", nameof(folderKey));

            if (FolderLinks.Any(fl => fl.FolderKey == folderKey))
                return;

            FolderLinks.Add(new MarketingActionFolderLink
            {
                MarketingActionId = Id,
                FolderKey = folderKey.Trim(),
                FolderType = folderType,
                CreatedAt = utcNow,
            });
        }

        public void SoftDelete(string userId, string username)
        {
            IsDeleted = true;
            DeletedAt = DateTime.UtcNow;
            DeletedByUserId = userId;
            DeletedByUsername = username;
            ModifiedAt = DateTime.UtcNow;
            ModifiedByUserId = userId;
            ModifiedByUsername = username;
        }

        public void MarkOutlookSynced(string eventId, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(eventId))
                throw new ArgumentException("Event ID cannot be empty", nameof(eventId));

            OutlookEventId = eventId;
            OutlookLastAttemptAt = utcNow;
            OutlookSyncStatus = MarketingSyncStatus.Synced;
            OutlookSyncError = null;
        }

        public void ClearOutlookLink()
        {
            OutlookEventId = null;
            OutlookLastAttemptAt = null;
            OutlookSyncStatus = MarketingSyncStatus.NotSynced;
            OutlookSyncError = null;
        }

        public void UpdateDetails(
            string title,
            string? description,
            MarketingActionType actionType,
            DateTime startDate,
            DateTime? endDate,
            string modifiedByUserId,
            string? modifiedByUsername,
            DateTime utcNow)
        {
            Title = NormalizeTitle(title);
            Description = NormalizeDescription(description);
            ActionType = actionType;
            StartDate = startDate;
            EndDate = endDate;
            ModifiedAt = utcNow;
            ModifiedByUserId = modifiedByUserId;
            ModifiedByUsername = modifiedByUsername ?? "Unknown User";
        }

        private static string NormalizeTitle(string? raw) => (raw ?? string.Empty).Trim();

        private static string? NormalizeDescription(string? raw) => raw?.Trim();
    }
}
