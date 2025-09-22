namespace Anela.Heblo.IssuedInvoices
{
    public class Customer
    {
        public object Guid { get; set; }

        public string Phone { get; set; }

        public string Email { get; set; }

        public string CompanyId { get; set; }

        public string VatId { get; set; }

        public object ClientCode { get; set; }

        public string? Company { get; set; }
        public string Name { get; set; }
        public string DisplayName => string.IsNullOrEmpty(Company) ? Name : $"{Company} - {Name}";

        //public string BankAccount { get; set; }

        //public string Iban { get; set; }

        //public string Bic { get; set; }

        //public string TaxMode { get; set; }
        //public bool? VatPayer { get; set; }
    }
}