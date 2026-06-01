namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class TagRuleDto
    {
        public int Id { get; set; }
        public string PathPattern { get; set; } = null!;
        public string TagName { get; set; } = null!;
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }
}
