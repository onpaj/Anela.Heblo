namespace Anela.Heblo.Domain.Features.Journal
{
    public class JournalQueryCriteria
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string SortBy { get; set; } = "EntryDate";
        public string SortDirection { get; set; } = "DESC";
    }
}