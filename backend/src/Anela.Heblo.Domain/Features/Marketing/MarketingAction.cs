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
        public void AssociateWithProduct(string productCode)
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

        /// <summary>
        /// Atomically replaces the full set of product associations.
        /// A <c>null</c> sequence is treated as empty (clears all associations).
        /// Each entry is trimmed and upper-cased (invariant) before dedup.
        /// Throws <see cref="ArgumentException"/> if any entry is null, empty, or whitespace.
        /// </summary>
        public void ReplaceProductAssociations(IEnumerable<string>? productCodes, DateTime utcNow)
        {
            var normalized = new List<string>();
            if (productCodes != null)
            {
                foreach (var raw in productCodes)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                        throw new ArgumentException("Product code cannot be empty", nameof(productCodes));

                    var code = raw.Trim().ToUpperInvariant();
                    if (!normalized.Contains(code))
                        normalized.Add(code);
                }
            }

            ProductAssociations.Clear();
            foreach (var code in normalized)
            {
                ProductAssociations.Add(new MarketingActionProduct
                {
                    MarketingActionId = Id,
                    ProductCodePrefix = code,
                    CreatedAt = utcNow,
                });
            }
        }

        /// <summary>
        /// Atomically replaces the full set of folder links.
        /// A <c>null</c> sequence is treated as empty (clears all links).
        /// <paramref name="links"/> folderKey is trimmed before dedup.
        /// Deduplicates by the composite key (<c>folderKey</c>, <c>folderType</c>) —
        /// this is stricter than <see cref="LinkToFolder"/>, which dedupes by
        /// <c>folderKey</c> alone. The asymmetry is intentional; new code should
        /// use this method when replacing the full set.
        /// Throws <see cref="ArgumentException"/> if any entry's
        /// folderKey is null, empty, or whitespace.
        /// </summary>
        public void ReplaceFolderLinks(
            IEnumerable<(string folderKey, MarketingFolderType folderType)>? links,
            DateTime utcNow)
        {
            var normalized = new List<(string folderKey, MarketingFolderType folderType)>();
            if (links != null)
            {
                foreach (var (rawKey, type) in links)
                {
                    if (string.IsNullOrWhiteSpace(rawKey))
                        throw new ArgumentException("Folder key cannot be empty", nameof(links));

                    var key = rawKey.Trim();
                    if (!normalized.Any(n => n.folderKey == key && n.folderType == type))
                        normalized.Add((key, type));
                }
            }

            FolderLinks.Clear();
            foreach (var (key, type) in normalized)
            {
                FolderLinks.Add(new MarketingActionFolderLink
                {
                    MarketingActionId = Id,
                    FolderKey = key,
                    FolderType = type,
                    CreatedAt = utcNow,
                });
            }
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
