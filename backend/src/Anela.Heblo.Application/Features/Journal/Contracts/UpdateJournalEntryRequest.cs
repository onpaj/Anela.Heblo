using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class UpdateJournalEntryRequest : IRequest<UpdateJournalEntryResponse>
    {
        public int Id { get; set; }

        [MaxLength(200)]
        public string? Title { get; set; }

        [Required]
        [MaxLength(10000)]
        public string Content { get; set; } = null!;

        [Required]
        public DateTime EntryDate { get; set; }

        public List<string>? AssociatedProducts { get; set; }
        public List<int>? TagIds { get; set; }
    }

    public class UpdateJournalEntryResponse : BaseResponse
    {
        public int Id { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string Message { get; set; } = "Journal entry updated successfully";

        public UpdateJournalEntryResponse() : base() { }
        public UpdateJournalEntryResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
    }
}