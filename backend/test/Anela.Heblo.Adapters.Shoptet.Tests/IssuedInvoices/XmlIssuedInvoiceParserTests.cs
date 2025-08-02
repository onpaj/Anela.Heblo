using Anela.Heblo.Adapters.Shoptet.IssuedInvoices;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Adapters.Shoptet.Tests.IssuedInvoices;

[Trait("Category", "Unit")]
public class XmlIssuedInvoiceParserTests
{
    private readonly Mock<IMapper> _mockMapper;
    private readonly Mock<ILogger<XmlIssuedInvoiceParser>> _mockLogger;
    private readonly XmlIssuedInvoiceParser _parser;

    public XmlIssuedInvoiceParserTests()
    {
        _mockMapper = new Mock<IMapper>();
        _mockLogger = new Mock<ILogger<XmlIssuedInvoiceParser>>();
        _parser = new XmlIssuedInvoiceParser(_mockMapper.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ParseAsync_WithValidXmlData_ReturnsIssuedInvoiceDetails()
    {
        // Arrange
        var validXml = @"<?xml version='1.0' encoding='UTF-8'?>
<dat:dataPack version='2.0' ico='12345678' application='TestApp' xmlns:dat='http://www.stormware.cz/schema/version_2/data.xsd' xmlns:inv='http://www.stormware.cz/schema/version_2/invoice.xsd' xmlns:typ='http://www.stormware.cz/schema/version_2/type.xsd'>
    <dat:dataPackItem version='2.0' id='1'>
        <inv:invoice version='2.0'>
            <inv:invoiceHeader>
                <inv:invoiceType>issuedInvoice</inv:invoiceType>
                <inv:number>
                    <typ:numberRequested>INV001</typ:numberRequested>
                </inv:number>
                <inv:date>2024-01-15</inv:date>
                <inv:dateTax>2024-01-15</inv:dateTax>
                <inv:dateDue>2024-01-30</inv:dateDue>
                <inv:partnerIdentity>
                    <typ:address>
                        <typ:company>Test Company</typ:company>
                        <typ:name>John Doe</typ:name>
                        <typ:city>Prague</typ:city>
                        <typ:street>Main Street 123</typ:street>
                        <typ:zip>10000</typ:zip>
                        <typ:ico>87654321</typ:ico>
                        <typ:dic>CZ87654321</typ:dic>
                    </typ:address>
                </inv:partnerIdentity>
            </inv:invoiceHeader>
            <inv:invoiceDetail>
                <inv:invoiceItem>
                    <inv:text>Test Product</inv:text>
                    <inv:code>PROD001</inv:code>
                    <inv:quantity>2</inv:quantity>
                    <inv:unit>ks</inv:unit>
                    <inv:payVAT>false</inv:payVAT>
                    <inv:rateVAT>high</inv:rateVAT>
                    <inv:homeCurrency>
                        <typ:unitPrice>100.00</typ:unitPrice>
                        <typ:price>200.00</typ:price>
                        <typ:priceVAT>242.00</typ:priceVAT>
                    </inv:homeCurrency>
                </inv:invoiceItem>
            </inv:invoiceDetail>
            <inv:invoiceSummary>
                <inv:roundingDocument>none</inv:roundingDocument>
                <inv:homeCurrency>
                    <typ:priceNone>200.00</typ:priceNone>
                    <typ:priceHighSum>242.00</typ:priceHighSum>
                </inv:homeCurrency>
            </inv:invoiceSummary>
        </inv:invoice>
    </dat:dataPackItem>
</dat:dataPack>";

        var expectedInvoiceDetail = new IssuedInvoiceDetail { Code = "INV001" };

        _mockMapper.Setup(m => m.Map<IssuedInvoiceDetail>(It.IsAny<Invoice>()))
                   .Returns(expectedInvoiceDetail);

        // Act
        var result = await _parser.ParseAsync(validXml);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result[0].Should().Be(expectedInvoiceDetail);

        _mockMapper.Verify(m => m.Map<IssuedInvoiceDetail>(It.IsAny<Invoice>()), Times.Once);
    }

    [Fact]
    public async Task ParseAsync_WithMultipleValidInvoices_ReturnsAllInvoiceDetails()
    {
        // Arrange
        var xmlWithMultipleInvoices = @"<?xml version='1.0' encoding='UTF-8'?>
<dat:dataPack version='2.0' ico='12345678' application='TestApp' xmlns:dat='http://www.stormware.cz/schema/version_2/data.xsd' xmlns:inv='http://www.stormware.cz/schema/version_2/invoice.xsd' xmlns:typ='http://www.stormware.cz/schema/version_2/type.xsd'>
    <dat:dataPackItem version='2.0' id='1'>
        <inv:invoice version='2.0'>
            <inv:invoiceHeader>
                <inv:invoiceType>issuedInvoice</inv:invoiceType>
                <inv:number><typ:numberRequested>INV001</typ:numberRequested></inv:number>
                <inv:date>2024-01-15</inv:date>
            </inv:invoiceHeader>
            <inv:invoiceDetail>
                <inv:invoiceItem>
                    <inv:text>Product 1</inv:text>
                </inv:invoiceItem>
            </inv:invoiceDetail>
            <inv:invoiceSummary>
                <inv:roundingDocument>none</inv:roundingDocument>
            </inv:invoiceSummary>
        </inv:invoice>
    </dat:dataPackItem>
    <dat:dataPackItem version='2.0' id='2'>
        <inv:invoice version='2.0'>
            <inv:invoiceHeader>
                <inv:invoiceType>issuedInvoice</inv:invoiceType>
                <inv:number><typ:numberRequested>INV002</typ:numberRequested></inv:number>
                <inv:date>2024-01-16</inv:date>
            </inv:invoiceHeader>
            <inv:invoiceDetail>
                <inv:invoiceItem>
                    <inv:text>Product 2</inv:text>
                </inv:invoiceItem>
            </inv:invoiceDetail>
            <inv:invoiceSummary>
                <inv:roundingDocument>none</inv:roundingDocument>
            </inv:invoiceSummary>
        </inv:invoice>
    </dat:dataPackItem>
</dat:dataPack>";

        var invoice1 = new IssuedInvoiceDetail { Code = "INV001" };
        var invoice2 = new IssuedInvoiceDetail { Code = "INV002" };

        _mockMapper.SetupSequence(m => m.Map<IssuedInvoiceDetail>(It.IsAny<Invoice>()))
                   .Returns(invoice1)
                   .Returns(invoice2);

        // Act
        var result = await _parser.ParseAsync(xmlWithMultipleInvoices);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Should().Be(invoice1);
        result[1].Should().Be(invoice2);

        _mockMapper.Verify(m => m.Map<IssuedInvoiceDetail>(It.IsAny<Invoice>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ParseAsync_WithInvalidInvoice_SkipsInvalidAndLogsError()
    {
        // Arrange
        var xmlWithInvalidInvoice = @"<?xml version='1.0' encoding='UTF-8'?>
<dat:dataPack version='2.0' ico='12345678' application='TestApp' xmlns:dat='http://www.stormware.cz/schema/version_2/data.xsd' xmlns:inv='http://www.stormware.cz/schema/version_2/invoice.xsd' xmlns:typ='http://www.stormware.cz/schema/version_2/type.xsd'>
    <dat:dataPackItem version='2.0' id='1'>
        <inv:invoice version='2.0'>
            <inv:invoiceHeader>
                <inv:invoiceType>issuedInvoice</inv:invoiceType>
            </inv:invoiceHeader>
            <!-- Missing required invoiceDetail and invoiceSummary -->
        </inv:invoice>
    </dat:dataPackItem>
    <dat:dataPackItem version='2.0' id='2'>
        <inv:invoice version='2.0'>
            <inv:invoiceHeader>
                <inv:invoiceType>issuedInvoice</inv:invoiceType>
                <inv:number><typ:numberRequested>INV002</typ:numberRequested></inv:number>
            </inv:invoiceHeader>
            <inv:invoiceDetail>
                <inv:invoiceItem>
                    <inv:text>Valid Product</inv:text>
                </inv:invoiceItem>
            </inv:invoiceDetail>
            <inv:invoiceSummary>
                <inv:roundingDocument>none</inv:roundingDocument>
            </inv:invoiceSummary>
        </inv:invoice>
    </dat:dataPackItem>
</dat:dataPack>";

        var validInvoice = new IssuedInvoiceDetail { Code = "INV002" };
        _mockMapper.Setup(m => m.Map<IssuedInvoiceDetail>(It.IsAny<Invoice>()))
                   .Returns(validInvoice);

        // Act
        var result = await _parser.ParseAsync(xmlWithInvalidInvoice);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result[0].Should().Be(validInvoice);

        // Verify that error was logged for invalid invoice
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Unable to deserialize invoice 1")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _mockMapper.Verify(m => m.Map<IssuedInvoiceDetail>(It.IsAny<Invoice>()), Times.Once);
    }

    [Fact]
    public async Task ParseAsync_WithEmptyDataPack_ReturnsEmptyList()
    {
        // Arrange
        var emptyXml = @"<?xml version='1.0' encoding='UTF-8'?>
<dat:dataPack version='2.0' ico='12345678' application='TestApp' xmlns:dat='http://www.stormware.cz/schema/version_2/data.xsd' xmlns:inv='http://www.stormware.cz/schema/version_2/invoice.xsd' xmlns:typ='http://www.stormware.cz/schema/version_2/type.xsd'>
</dat:dataPack>";

        // Act
        var result = await _parser.ParseAsync(emptyXml);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();

        _mockMapper.Verify(m => m.Map<IssuedInvoiceDetail>(It.IsAny<Invoice>()), Times.Never);
    }

    [Fact]
    public async Task ParseAsync_WithMalformedXml_ThrowsInvalidOperationExceptionException()
    {
        // Arrange
        var malformedXml = @"<?xml version='1.0' encoding='UTF-8'?>
<dat:dataPack version='2.0' ico='12345678' application='TestApp' xmlns:dat='http://www.stormware.cz/schema/version_2/data.xsd'>
    <dat:dataPackItem version='2.0' id='1'>
        <unclosed-tag>
    </dat:dataPackItem>
</dat:dataPack>";

        // Act & Assert
        var act = async () => await _parser.ParseAsync(malformedXml);
        await act.Should().ThrowAsync<InvalidOperationException>();

        _mockMapper.Verify(m => m.Map<IssuedInvoiceDetail>(It.IsAny<Invoice>()), Times.Never);
    }

    [Fact]
    public async Task ParseAsync_WithNullInput_ThrowsArgumentException()
    {
        // Act & Assert
        var act = async () => await _parser.ParseAsync(null!);
        await act.Should().ThrowAsync<ArgumentException>();

        _mockMapper.Verify(m => m.Map<IssuedInvoiceDetail>(It.IsAny<Invoice>()), Times.Never);
    }

    [Fact]
    public async Task ParseAsync_WithEmptyInput_ThrowsInvalidOperationException()
    {
        // Act & Assert
        var act = async () => await _parser.ParseAsync(string.Empty);
        await act.Should().ThrowAsync<InvalidOperationException>();

        _mockMapper.Verify(m => m.Map<IssuedInvoiceDetail>(It.IsAny<Invoice>()), Times.Never);
    }

    [Fact]
    public async Task ParseAsync_WithComplexInvoiceStructure_ParsesAllFields()
    {
        // Arrange
        var complexXml = @"<?xml version='1.0' encoding='UTF-8'?>
<dat:dataPack version='2.0' ico='12345678' application='TestApp' xmlns:dat='http://www.stormware.cz/schema/version_2/data.xsd' xmlns:inv='http://www.stormware.cz/schema/version_2/invoice.xsd' xmlns:typ='http://www.stormware.cz/schema/version_2/type.xsd'>
    <dat:dataPackItem version='2.0' id='1'>
        <inv:invoice version='2.0'>
            <inv:invoiceHeader>
                <inv:invoiceType>issuedInvoice</inv:invoiceType>
                <inv:number>
                    <typ:numberRequested>INV-2024-001</typ:numberRequested>
                </inv:number>
                <inv:paymentType>
                    <typ:paymentType>card</typ:paymentType>
                </inv:paymentType>
                <inv:carrier>
                    <typ:ids>DHL</typ:ids>
                </inv:carrier>
                <inv:numberOrder>ORD-2024-001</inv:numberOrder>
                <inv:symVar>1234567890</inv:symVar>
                <inv:date>2024-01-15</inv:date>
                <inv:dateTax>2024-01-15</inv:dateTax>
                <inv:dateDue>2024-02-14</inv:dateDue>
                <inv:partnerIdentity>
                    <typ:address>
                        <typ:company>Test Company s.r.o.</typ:company>
                        <typ:name>John Doe</typ:name>
                        <typ:city>Prague</typ:city>
                        <typ:street>Wenceslas Square 1</typ:street>
                        <typ:zip>11000</typ:zip>
                        <typ:country>
                            <typ:ids>CZ</typ:ids>
                        </typ:country>
                        <typ:ico>12345678</typ:ico>
                        <typ:dic>CZ12345678</typ:dic>
                    </typ:address>
                    <typ:shipToAddress>
                        <typ:company>Test Company - Warehouse</typ:company>
                        <typ:name>Jane Smith</typ:name>
                        <typ:city>Brno</typ:city>
                        <typ:street>Freedom Square 2</typ:street>
                        <typ:zip>60200</typ:zip>
                        <typ:country>
                            <typ:ids>CZ</typ:ids>
                        </typ:country>
                    </typ:shipToAddress>
                </inv:partnerIdentity>
            </inv:invoiceHeader>
            <inv:invoiceDetail>
                <inv:invoiceItem>
                    <inv:text>Product A - Premium Quality</inv:text>
                    <inv:code>PROD-A-001</inv:code>
                    <inv:quantity>3</inv:quantity>
                    <inv:unit>pcs</inv:unit>
                    <inv:payVAT>true</inv:payVAT>
                    <inv:rateVAT>high</inv:rateVAT>
                    <inv:discountPercentage>10</inv:discountPercentage>
                    <inv:homeCurrency>
                        <typ:unitPrice>150.00</typ:unitPrice>
                        <typ:price>405.00</typ:price>
                        <typ:priceVAT>490.05</typ:priceVAT>
                    </inv:homeCurrency>
                    <inv:stockItem>
                        <typ:stockItem>
                            <typ:ids>STOCK-A-001</typ:ids>
                        </typ:stockItem>
                    </inv:stockItem>
                </inv:invoiceItem>
                <inv:invoiceItem>
                    <inv:text>Service Fee</inv:text>
                    <inv:code>SVC-001</inv:code>
                    <inv:quantity>1</inv:quantity>
                    <inv:unit>service</inv:unit>
                    <inv:payVAT>true</inv:payVAT>
                    <inv:rateVAT>high</inv:rateVAT>
                    <inv:homeCurrency>
                        <typ:unitPrice>50.00</typ:unitPrice>
                        <typ:price>50.00</typ:price>
                        <typ:priceVAT>60.50</typ:priceVAT>
                    </inv:homeCurrency>
                </inv:invoiceItem>
            </inv:invoiceDetail>
            <inv:invoiceSummary>
                <inv:roundingDocument>mathematical</inv:roundingDocument>
                <inv:homeCurrency>
                    <typ:priceNone>0.00</typ:priceNone>
                    <typ:priceHighSum>550.55</typ:priceHighSum>
                    <typ:round>
                        <typ:priceRound>0.00</typ:priceRound>
                    </typ:round>
                </inv:homeCurrency>
            </inv:invoiceSummary>
        </inv:invoice>
    </dat:dataPackItem>
</dat:dataPack>";

        var expectedInvoice = new IssuedInvoiceDetail { Code = "INV-2024-001" };
        _mockMapper.Setup(m => m.Map<IssuedInvoiceDetail>(It.IsAny<Invoice>()))
                   .Returns(expectedInvoice);

        // Act
        var result = await _parser.ParseAsync(complexXml);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result[0].Should().Be(expectedInvoice);

        // Verify that mapper was called with properly parsed invoice structure
        _mockMapper.Verify(m => m.Map<IssuedInvoiceDetail>(It.Is<Invoice>(inv =>
            inv.InvoiceHeader != null &&
            inv.InvoiceHeader.Number != null &&
            inv.InvoiceHeader.Number.NumberRequested == "INV-2024-001" &&
            inv.InvoiceDetail != null &&
            inv.InvoiceDetail.InvoiceItems != null &&
            inv.InvoiceDetail.InvoiceItems.Count == 2 &&
            inv.InvoiceSummary != null
        )), Times.Once);
    }
}