using System;
using System.Collections.Generic;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Marketing.Contracts
{
    public class ImportFromOutlookRequest : IRequest<ImportFromOutlookResponse>
    {
        public DateTime FromUtc { get; set; }

        public DateTime ToUtc { get; set; }

        public bool DryRun { get; set; }
    }

    public class ImportFromOutlookResponse : BaseResponse
    {
        public int Created { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public List<string> UnmappedCategories { get; set; } = new();
        public List<ImportedItemDto> Items { get; set; } = new();

        public ImportFromOutlookResponse() : base() { }

        public ImportFromOutlookResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
            : base(errorCode, parameters) { }
    }

    public class ImportedItemDto
    {
        public string OutlookEventId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;

        /// <summary>
        /// One of <see cref="ImportStatus.Created"/>, <see cref="ImportStatus.WouldCreate"/>,
        /// <see cref="ImportStatus.Skipped"/>, or <see cref="ImportStatus.Failed"/>.
        /// </summary>
        public string Status { get; set; } = string.Empty;
        public string? Error { get; set; }
        public int? CreatedActionId { get; set; }
    }

    public static class ImportStatus
    {
        public const string Created = "Created";
        public const string WouldCreate = "WouldCreate";
        public const string Skipped = "Skipped";
        public const string Failed = "Failed";
    }
}
