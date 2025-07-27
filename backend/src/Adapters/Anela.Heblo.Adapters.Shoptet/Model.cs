using System.Xml.Serialization;

namespace Anela.Heblo.Adapters.Shoptet
{
	[XmlRoot(ElementName="number", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
	public class Number {
		[XmlElement(ElementName="numberRequested", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string NumberRequested { get; set; }
	}

	[XmlRoot(ElementName="paymentType", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
	public class PaymentTypeClass {
		[XmlElement(ElementName="paymentType", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string PaymentType { get; set; }
	}

	[XmlRoot(ElementName="carrier", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
	public class Carrier {
		[XmlElement(ElementName="ids", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Ids { get; set; }
	}

	[XmlRoot(ElementName="country", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
	public class Country {
		[XmlElement(ElementName="ids", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Ids { get; set; }
	}

	[XmlRoot(ElementName="address", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
	public class Address {
		[XmlElement(ElementName="company", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Company { get; set; }
		[XmlElement(ElementName="name", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Name { get; set; }
		[XmlElement(ElementName="city", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string City { get; set; }
		[XmlElement(ElementName="street", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Street { get; set; }
		[XmlElement(ElementName="zip", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Zip { get; set; }
		[XmlElement(ElementName="country", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public Country Country { get; set; }
		[XmlElement(ElementName="ico", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Ico { get; set; }
		[XmlElement(ElementName="dic", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Dic { get; set; }
	}

	[XmlRoot(ElementName="partnerIdentity", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
	public class PartnerIdentity {
		[XmlElement(ElementName="address", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public Address Address { get; set; }
		[XmlElement(ElementName="shipToAddress", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public ShipToAddress ShipToAddress { get; set; }
	}

	[XmlRoot(ElementName="invoiceHeader", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
	public class InvoiceHeader {
		[XmlElement(ElementName="invoiceType", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public string InvoiceType { get; set; }
		[XmlElement(ElementName="number", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public Number Number { get; set; }
		[XmlElement(ElementName="paymentType", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public PaymentTypeClass PaymentType { get; set; }
		[XmlElement(ElementName="carrier", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public Carrier Carrier { get; set; }
		[XmlElement(ElementName="numberOrder", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public string NumberOrder { get; set; }
		[XmlElement(ElementName="symVar", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public string SymVar { get; set; }
		[XmlElement(ElementName="date", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public string Date { get; set; }
		[XmlElement(ElementName="dateTax", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public string DateTax { get; set; }
		[XmlElement(ElementName="dateDue", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public string DateDue { get; set; }
		[XmlElement(ElementName="partnerIdentity", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public PartnerIdentity PartnerIdentity { get; set; }
	}

	[XmlRoot(ElementName="homeCurrency", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
	public class HomeCurrency {
		[XmlElement(ElementName="unitPrice", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public decimal UnitPrice { get; set; }
		[XmlElement(ElementName="price", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public decimal Price { get; set; }
		[XmlElement(ElementName="priceVAT", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public decimal PriceVAT { get; set; }
		[XmlElement(ElementName="round", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public Round Round { get; set; }
		[XmlElement(ElementName="priceHighSum", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public decimal PriceHighSum { get; set; }
		[XmlElement(ElementName="priceNone", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public decimal PriceNone { get; set; }
	}
	
	[XmlRoot(ElementName="foreignCurrency", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
	public class ForeignCurrency {
		[XmlElement(ElementName="unitPrice", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public decimal UnitPrice { get; set; }
		[XmlElement(ElementName="price", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public decimal Price { get; set; }
		[XmlElement(ElementName="priceVAT", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public decimal PriceVAT { get; set; }
		[XmlElement(ElementName="currency", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public Currency Currency { get; set; }
		[XmlElement(ElementName="rate", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public decimal Rate { get; set; }
		[XmlElement(ElementName="amount", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Amount { get; set; }
		[XmlElement(ElementName="priceSum", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public decimal PriceSum { get; set; }
	}

	[XmlRoot(ElementName="currency", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
	public class Currency {
		[XmlElement(ElementName="ids", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Ids { get; set; }
	}
	
	[XmlRoot(ElementName="invoiceItem", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
	public class InvoiceItem {
		[XmlElement(ElementName="text", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public string Text { get; set; }
		[XmlElement(ElementName="quantity", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public string Quantity { get; set; }
		[XmlElement(ElementName="unit", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public string Unit { get; set; }
		[XmlElement(ElementName="payVAT", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public string PayVAT { get; set; }
		[XmlElement(ElementName="rateVAT", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public string RateVAT { get; set; }
		[XmlElement(ElementName="homeCurrency", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public HomeCurrency HomeCurrency { get; set; }
		[XmlElement(ElementName="foreignCurrency", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public ForeignCurrency ForeignCurrency { get; set; }
		[XmlElement(ElementName="code", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public string Code { get; set; }
		[XmlElement(ElementName="stockItem", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public StockItem2 StockItem2 { get; set; }
		[XmlElement(ElementName="discountPercentage", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public string DiscountPercentage { get; set; }
	}

	[XmlRoot(ElementName="invoiceDetail", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
	public class InvoiceDetail {
		[XmlElement(ElementName="invoiceItem", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public List<InvoiceItem> InvoiceItems { get; set; }
	}

	[XmlRoot(ElementName="round", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
	public class Round {
		[XmlElement(ElementName="priceRound", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string PriceRound { get; set; }
	}

	[XmlRoot(ElementName="invoiceSummary", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
	public class InvoiceSummary {
		[XmlElement(ElementName="roundingDocument", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public string RoundingDocument { get; set; }
		[XmlElement(ElementName="homeCurrency", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public HomeCurrency HomeCurrency { get; set; }
		[XmlElement(ElementName="foreignCurrency", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public ForeignCurrency ForeignCurrency { get; set; }
	}

	[XmlRoot(ElementName="invoice", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
	public class Invoice {
		[XmlElement(ElementName="invoiceHeader", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public InvoiceHeader? InvoiceHeader { get; set; }
		[XmlElement(ElementName="invoiceDetail", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public InvoiceDetail? InvoiceDetail { get; set; }
		[XmlElement(ElementName="invoiceSummary", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public InvoiceSummary? InvoiceSummary { get; set; }
		[XmlAttribute(AttributeName="version")]
		public string Version { get; set; }
	}

	[XmlRoot(ElementName="dataPackItem", Namespace="http://www.stormware.cz/schema/version_2/data.xsd")]
	public class DataPackItem {
		[XmlElement(ElementName="invoice", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
		public Invoice? Invoice { get; set; }
		[XmlAttribute(AttributeName="id")]
		public string Id { get; set; }
		[XmlAttribute(AttributeName="version")]
		public string Version { get; set; }

		public bool IsValid()
		{
			return Invoice is { InvoiceHeader: not null, InvoiceDetail: not null, InvoiceSummary: not null };
		}
	}

	[XmlRoot(ElementName="stockItem", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
	public class StockItem {
		[XmlElement(ElementName="ids", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Ids { get; set; }
	}

	[XmlRoot(ElementName="stockItem", Namespace="http://www.stormware.cz/schema/version_2/invoice.xsd")]
	public class StockItem2 {
		[XmlElement(ElementName="stockItem", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public StockItem StockItem { get; set; }
	}

	[XmlRoot(ElementName="shipToAddress", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
	public class ShipToAddress {
		[XmlElement(ElementName="company", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Company { get; set; }
		[XmlElement(ElementName="name", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Name { get; set; }
		[XmlElement(ElementName="city", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string City { get; set; }
		[XmlElement(ElementName="street", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Street { get; set; }
		[XmlElement(ElementName="zip", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public string Zip { get; set; }
		[XmlElement(ElementName="country", Namespace="http://www.stormware.cz/schema/version_2/type.xsd")]
		public Country Country { get; set; }
	}

	[XmlRoot(ElementName="dataPack", Namespace="http://www.stormware.cz/schema/version_2/data.xsd")]
	public class DataPack {
		[XmlElement(ElementName="dataPackItem", Namespace="http://www.stormware.cz/schema/version_2/data.xsd")]
		public List<DataPackItem> DataPackItems { get; set; }
		[XmlAttribute(AttributeName="id")]
		public string Id { get; set; }
		[XmlAttribute(AttributeName="ico")]
		public string Ico { get; set; }
		[XmlAttribute(AttributeName="application")]
		public string Application { get; set; }
		[XmlAttribute(AttributeName="version")]
		public string Version { get; set; }
		[XmlAttribute(AttributeName="note")]
		public string Note { get; set; }
		[XmlAttribute(AttributeName="dat", Namespace="http://www.w3.org/2000/xmlns/")]
		public string Dat { get; set; }
		[XmlAttribute(AttributeName="inv", Namespace="http://www.w3.org/2000/xmlns/")]
		public string Inv { get; set; }
		[XmlAttribute(AttributeName="typ", Namespace="http://www.w3.org/2000/xmlns/")]
		public string Typ { get; set; }
	}
}
