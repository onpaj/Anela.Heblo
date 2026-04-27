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
        public string Title { get; set; } = null!;

        [MaxLength(5000)]
        public string? Description { get; set; }

        public MarketingActionType ActionType { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        [Required]
        public DateTime ModifiedAt { get; set; }

        [Required]
        [MaxLength(100)]
        public string CreatedByUserId { get; set; } = null!;

        [MaxLength(100)]
        public string? CreatedByUsername { get; set; }

        [MaxLength(100)]
        public string? ModifiedByUserId { get; set; }

        [MaxLength(100)]
        public string? ModifiedByUsername { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

        [MaxLength(100)]
        public string? DeletedByUserId { get; set; }

        [MaxLength(100)]
        public string? DeletedByUsername { get; set; }

        // Outlook sync fields
        [MaxLength(500)]
        public string? OutlookEventId { get; set; }

        public DateTime? OutlookSyncedAt { get; set; }

        public MarketingSyncStatus OutlookSyncStatus { get; set; } = MarketingSyncStatus.NotSynced;

        [MaxLength(1000)]
        public string? OutlookSyncError { get; set; }

        // Navigation properties
        public virtual ICollection<MarketingActionProduct> ProductAssociations { get; set; } = new List<MarketingActionProduct>();
        public virtual ICollection<MarketingActionFolderLink> FolderLinks { get; set; } = new List<MarketingActionFolderLink>();

        // Domain methods
        public void AssociateWithProduct(string productCode)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                throw new ArgumentException("Product code cannot be empty", nameof(productCode));

            if (ProductAssociations.Any(pa => pa.ProductCodePrefix == productCode))
                return;

            ProductAssociations.Add(new MarketingActionProduct
            {
                MarketingActionId = Id,
                ProductCodePrefix = productCode.Trim().ToUpperInvariant(),
                CreatedAt = DateTime.UtcNow,
            });
        }

        public void LinkToFolder(string folderKey, MarketingFolderType folderType)
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
                CreatedAt = DateTime.UtcNow,
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
            OutlookSyncedAt = utcNow;
            OutlookSyncStatus = MarketingSyncStatus.Synced;
            OutlookSyncError = null;
        }

        public void MarkOutlookFailed(string error, DateTime utcNow)
        {
            const int maxErrorLength = 1000;

            OutlookSyncStatus = MarketingSyncStatus.Failed;
            OutlookSyncedAt = utcNow;
            OutlookSyncError = error?.Length > maxErrorLength
                ? error[..maxErrorLength]
                : error;
        }

        public void ClearOutlookLink()
        {
            OutlookEventId = null;
            OutlookSyncedAt = null;
            OutlookSyncStatus = MarketingSyncStatus.NotSynced;
            OutlookSyncError = null;
        }
    }
}
