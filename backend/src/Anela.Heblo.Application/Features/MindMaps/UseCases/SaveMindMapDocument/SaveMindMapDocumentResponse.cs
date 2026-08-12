using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.SaveMindMapDocument;

public class SaveMindMapDocumentResponse : BaseResponse
{
    public string DocumentJson { get; set; } = null!;

    public SaveMindMapDocumentResponse() { }
    public SaveMindMapDocumentResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
