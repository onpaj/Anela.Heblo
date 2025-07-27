namespace Anela.Heblo.Adapters.Shoptet.DropBox
{
    public class DropBoxSourceOptions
    {
        public string Token { get; set; }
        public string InvoiceFolder { get; set; }
        public string ResultsFolder { get; set; }
        public string FailuresFolder { get; set; }
        public string LogsFolder { get; set; }
        public bool Enabled { get; set; } = false;
    }
}