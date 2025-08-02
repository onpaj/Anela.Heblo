using System.Xml;
using System.Xml.Serialization;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Shoptet.IssuedInvoices;

public class XmlIssuedInvoiceParser : IIssuedInvoiceParser
{
    private readonly IMapper _mapper;
    private readonly ILogger<XmlIssuedInvoiceParser> _logger;

    public XmlIssuedInvoiceParser(IMapper mapper, ILogger<XmlIssuedInvoiceParser> logger)
    {
        _mapper = mapper;
        _logger = logger;
    }

    public Task<List<IssuedInvoiceDetail>> ParseAsync(string data)
    {
        XmlSerializer serializer = new XmlSerializer(typeof(DataPack));
        var invoices = new List<IssuedInvoiceDetail>();

        using var reader = new XmlTextReader(new StringReader(data));

        var pack = (DataPack)serializer.Deserialize(reader);
        foreach (var i in pack.DataPackItems)
        {
            if (i.IsValid())
                invoices.Add(_mapper.Map<IssuedInvoiceDetail>(i.Invoice));
            else
                _logger.LogError("Unable to deserialize invoice {InvoicerNumber}", i.Id);
        }

        return Task.FromResult(invoices);
    }
}