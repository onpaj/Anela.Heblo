using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Marketing.Contracts
{
    public class ImportFromOutlookRequest : IRequest<ImportFromOutlookResponse>
    {
        [Required]
        public DateTime FromUtc { get; set; }

        [Required]
        public DateTime ToUtc { get; set; }

        public bool DryRun { get; set; }
    }

    public class ImportFromOutlookResponse : BaseResponse
    {
        public int Created { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public List<ImportedItemDto> Items { get; set; } = new();

        public ImportFromOutlookResponse() : base() { }

        public ImportFromOutlookResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
            : base(errorCode, parameters) { }
    }

    public class ImportedItemDto
    {
        public string OutlookEventId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Created" | "Skipped" | "Failed"
        public string? Error { get; set; }
        public int? CreatedActionId { get; set; }
    }
}
