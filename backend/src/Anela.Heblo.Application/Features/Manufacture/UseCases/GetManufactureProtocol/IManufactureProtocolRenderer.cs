namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;

public interface IManufactureProtocolRenderer
{
    byte[] Render(ManufactureProtocolData data);
}
