namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class AddRuleBody
    {
        public string PathPattern { get; set; } = null!;
        public string TagName { get; set; } = null!;
        public int SortOrder { get; set; }
    }
}
