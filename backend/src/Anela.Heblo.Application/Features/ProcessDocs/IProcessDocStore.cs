namespace Anela.Heblo.Application.Features.ProcessDocs;

public interface IProcessDocStore
{
    IReadOnlyList<ProcessDoc> All { get; }

    ProcessDoc? Find(string name);
}
