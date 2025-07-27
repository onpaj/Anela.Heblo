using Anela.Heblo.Invoices;
using Anela.Heblo.IssuedInvoices;
using Anela.Heblo.IssuedInvoices.Model;
using Dropbox.Api;
using Dropbox.Api.Files;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Anela.Heblo.Adapters.Shoptet.DropBox
{
    public class ShoptetDropBoxSource : IIssuedInvoiceSource
    {
        private readonly IIssuedInvoiceParser _invoiceParser;
        private readonly IOptions<DropBoxSourceOptions> _options;
        private readonly ILogger<ShoptetDropBoxSource> _logger;
        private readonly IMemoryCache _cache;
        private readonly DropboxClient _client;

        public ShoptetDropBoxSource(
            IIssuedInvoiceParser invoiceParser,
            IOptions<DropBoxSourceOptions> options,
            ILogger<ShoptetDropBoxSource> logger,
            IMemoryCache cache)
        {
            _invoiceParser = invoiceParser;
            _options = options;
            _logger = logger;
            _cache = cache;

            _client = new DropboxClient(_options.Value.Token);
        }

        public async Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(IssuedInvoiceSourceQuery query)
        {
            var batches = new List<IssuedInvoiceDetailBatch>();
            ListFolderResult batch = null;

            do
            {
                if (batch == null)
                    batch = await _client.Files.ListFolderAsync(_options.Value.InvoiceFolder);
                else
                    batch = await _client.Files.ListFolderContinueAsync(batch.Cursor);

                foreach (var f in batch.Entries.Where(w => w.IsFile && !w.IsDeleted))
                {
                    var file = await _client.Files.DownloadAsync(f.PathLower);
                    var content = await file.GetContentAsStringAsync();

                    var invoices = await _invoiceParser.ParseAsync(content);
                    var invoiceBatch = new IssuedInvoiceDetailBatch()
                    {
                        Invoices = invoices,
                        BatchId = f.Name
                    };
                    _cache.Set(invoiceBatch.BatchId, f);
                    batches.Add(invoiceBatch);
                }
            } while (batch.HasMore);

            return batches;
        }

        public async Task CommitAsync(IssuedInvoiceDetailBatch batch, string commitMessage)
        {
            var cached = _cache.Get<FileMetadata>(batch.BatchId);
            if (cached == null)
                throw new KeyNotFoundException($"Batch {batch.BatchId} not found in cache");

            await _client.Files.MoveV2Async(cached.PathLower, $"{_options.Value.ResultsFolder}/{cached.Name}.xml",autorename:true);
            await WriteLog(cached, commitMessage);
        }

        public async Task FailAsync(IssuedInvoiceDetailBatch batch, string errorMessage)
        {
            var cached = _cache.Get<FileMetadata>(batch.BatchId);
            if (cached == null)
                throw new KeyNotFoundException($"Batch {batch.BatchId} not found in cache");

            await _client.Files.MoveV2Async(cached.PathLower, $"{_options.Value.FailuresFolder}/{cached.Name}.xml", autorename: true);
            await WriteError(cached, errorMessage);
        }

        private Task WriteLog(FileMetadata metadata, string message)
        {
            return WriteLog(metadata, message, $"{_options.Value.LogsFolder}/{metadata.Name}.xml");
        }

        private Task WriteError(FileMetadata metadata, string message)
        {
            return WriteLog(metadata, message, $"{_options.Value.FailuresFolder}/{metadata.Name}_ERROR.xml");
        }


        private async Task WriteLog(FileMetadata metadata, string message, string filename)
        {
            Stream ms = new MemoryStream();

            await using (var streamWriter = new StreamWriter(ms))
            {
                using (var writer = new JsonTextWriter(streamWriter))
                {
                    await writer.WriteRawAsync(message);
                    await writer.FlushAsync();

                    if (ms.CanSeek)
                        ms.Seek(0, SeekOrigin.Begin);

                    await _client.Files.UploadAsync(filename, new WriteMode().AsOverwrite, autorename:true, body: ms);
                }
            }
        }
    }
}
