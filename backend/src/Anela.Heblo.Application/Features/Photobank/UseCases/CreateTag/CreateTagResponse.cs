namespace Anela.Heblo.Application.Features.Photobank.UseCases.CreateTag
{
    public class CreateTagResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public bool AlreadyExisted { get; set; }
    }
}
