using System;
using System.Collections.Generic;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class GetJournalEntriesRequest : IRequest<GetJournalEntriesResponse>
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string SortBy { get; set; } = "EntryDate";
        public string SortDirection { get; set; } = "DESC";
    }

    public class GetJournalEntriesResponse : BaseResponse
    {
        public List<JournalEntryDto> Entries { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public bool HasNextPage { get; set; }
        public bool HasPreviousPage { get; set; }
    }
}