using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class SearchJournalEntriesRequest : IRequest<SearchJournalEntriesResponse>
    {
        [MaxLength(200)]
        public string? SearchText { get; set; }

        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }

        public string? ProductCodePrefix { get; set; }
        public List<int>? TagIds { get; set; }

        [MaxLength(100)]
        public string? CreatedByUserId { get; set; }

        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        public string SortBy { get; set; } = "EntryDate";
        public string SortDirection { get; set; } = "DESC";
    }
}