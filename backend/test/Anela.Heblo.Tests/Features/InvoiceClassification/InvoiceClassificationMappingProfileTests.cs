using Anela.Heblo.Application.Features.InvoiceClassification;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using DomainTypes = Anela.Heblo.Domain.Features.InvoiceClassification;
using ContractTypes = Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class InvoiceClassificationMappingProfileTests
{
    private readonly IMapper _mapper;

    public InvoiceClassificationMappingProfileTests()
    {
        var config = new MapperConfiguration(
            cfg => cfg.AddProfile<InvoiceClassificationMappingProfile>(),
            NullLoggerFactory.Instance);
        config.AssertConfigurationIsValid();
        _mapper = config.CreateMapper();
    }

    [Fact]
    public void Map_AccountingTemplate_To_Dto_PreservesAllFields()
    {
        var source = new DomainTypes.AccountingTemplate
        {
            Code = "ACC-001",
            Name = "Office supplies",
            Description = "Pens, paper, etc.",
            AccountCode = "501100",
        };

        var dto = _mapper.Map<ContractTypes.AccountingTemplateDto>(source);

        dto.Code.Should().Be("ACC-001");
        dto.Name.Should().Be("Office supplies");
        dto.Description.Should().Be("Pens, paper, etc.");
        dto.AccountCode.Should().Be("501100");
    }

    [Fact]
    public void Map_ReceivedInvoice_To_Dto_PreservesAllFields()
    {
        var source = new DomainTypes.ReceivedInvoice
        {
            InvoiceNumber = "FV-2026-001",
            CompanyName = "Acme s.r.o.",
            CompanyVat = "CZ12345678",
            InvoiceDate = new DateTime(2026, 5, 26),
            DueDate = new DateTime(2026, 6, 9),
            TotalAmount = 12345.67m,
            Description = "Materials for May",
            AccountingTemplateCode = "ACC-001",
            DepartmentCode = "OPS",
            Labels = new[] { "auto", "review" },
            Items = new List<DomainTypes.ReceivedInvoiceItem>
            {
                new() { Code = "ITEM-1", Name = "Paper A4", Amount = 100m },
                new() { Code = "ITEM-2", Name = "Pens",     Amount = 50m  },
            },
        };

        var dto = _mapper.Map<ContractTypes.ReceivedInvoiceDto>(source);

        dto.InvoiceNumber.Should().Be("FV-2026-001");
        dto.CompanyName.Should().Be("Acme s.r.o.");
        dto.CompanyVat.Should().Be("CZ12345678");
        dto.InvoiceDate.Should().Be(new DateTime(2026, 5, 26));
        dto.DueDate.Should().Be(new DateTime(2026, 6, 9));
        dto.TotalAmount.Should().Be(12345.67m);
        dto.Description.Should().Be("Materials for May");
        dto.AccountingTemplateCode.Should().Be("ACC-001");
        dto.DepartmentCode.Should().Be("OPS");
        dto.Labels.Should().Equal("auto", "review");
        dto.Items.Should().HaveCount(2);
        dto.Items[0].Code.Should().Be("ITEM-1");
        dto.Items[0].Name.Should().Be("Paper A4");
        dto.Items[0].Amount.Should().Be(100m);
        dto.Items[1].Code.Should().Be("ITEM-2");
        dto.Items[1].Name.Should().Be("Pens");
        dto.Items[1].Amount.Should().Be(50m);
    }

    [Fact]
    public void Map_ReceivedInvoiceItem_To_Dto_PreservesAllFields()
    {
        var source = new DomainTypes.ReceivedInvoiceItem
        {
            Code = "ITEM-X",
            Name = "Widget",
            Amount = 42.5m,
        };

        var dto = _mapper.Map<ContractTypes.ReceivedInvoiceItemDto>(source);

        dto.Code.Should().Be("ITEM-X");
        dto.Name.Should().Be("Widget");
        dto.Amount.Should().Be(42.5m);
    }
}
