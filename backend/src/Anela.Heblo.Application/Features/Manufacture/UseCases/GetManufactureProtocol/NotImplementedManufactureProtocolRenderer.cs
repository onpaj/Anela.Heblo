namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;

/// <summary>
/// Placeholder renderer registered until Phase 6 installs the real QuestPDF implementation.
/// </summary>
internal sealed class NotImplementedManufactureProtocolRenderer : IManufactureProtocolRenderer
{
    public byte[] Render(ManufactureProtocolData data)
    {
        throw new NotImplementedException(
            "PDF rendering is not yet implemented. Register QuestPdfManufactureProtocolRenderer from the API project.");
    }
}
