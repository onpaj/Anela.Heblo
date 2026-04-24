namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class TagDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Source { get; set; }
    }

    public class TagWithCountDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public int Count { get; set; }
    }
}
